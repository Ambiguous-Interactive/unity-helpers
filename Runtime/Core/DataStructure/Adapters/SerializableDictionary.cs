// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// Portions of this file are adapted from JDSherbert's Unity-Serializable-Dictionary (MIT License):
// https://github.com/JDSherbert/Unity-Serializable-Dictionary

namespace WallstopStudios.UnityHelpers.Core.DataStructure.Adapters
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;
    using ProtoBuf;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Utils;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /// <summary>
    /// Provides the shared infrastructure for Unity-friendly serializable dictionary implementations.
    /// Manages the synchronized key and value arrays that Unity, ProtoBuf, and JSON rely on,
    /// while exposing a runtime dictionary for fast lookups and mutations.
    /// Derive from the generic base to create strongly typed dictionaries that stay editable in the inspector.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// [Serializable]
    /// public sealed class WeaponDefinition
    /// {
    ///     public string DisplayName;
    /// }
    ///
    /// [Serializable]
    /// public sealed class WeaponDefinitionCache : SerializableDictionary.Cache<WeaponDefinition>
    /// {
    /// }
    /// [Serializable]
    /// public sealed class WeaponDictionary
    ///     : SerializableDictionaryBase<int, WeaponDefinition, WeaponDefinitionCache>
    /// {
    ///     protected override WeaponDefinition GetValue(WeaponDefinitionCache[] cache, int index)
    ///     {
    ///         return cache[index].Data;
    ///     }
    ///     protected override void SetValue(WeaponDefinitionCache[] cache, int index, WeaponDefinition value)
    ///     {
    ///         cache[index] = new WeaponDefinitionCache { Data = value };
    ///     }
    /// }
    /// ]]></code>
    /// </example>
    [Serializable]
    public abstract class SerializableDictionaryBase
    {
        protected internal class Dictionary<TKey, TValue>
            : System.Collections.Generic.Dictionary<TKey, TValue>
        {
            /// <summary>
            /// Creates an empty runtime dictionary that uses the default equality comparer.
            /// </summary>
            /// <example>
            /// <code><![CDATA[
            /// protected sealed class AbilityDictionary
            ///     : SerializableDictionaryBase<string, AbilityDefinition, AbilityCache>
            /// {
            ///     public AbilityDictionary()
            ///         : base(new Dictionary<string, AbilityDefinition>())
            ///     {
            ///     }
            /// }
            /// ]]></code>
            /// </example>
            public Dictionary() { }

            /// <summary>
            /// Creates a runtime dictionary pre-populated with entries from another dictionary.
            /// </summary>
            /// <param name="dictionary">The source collection to copy.</param>
            /// <example>
            /// <code><![CDATA[
            /// IDictionary<string, AbilityDefinition> seed = new Dictionary<string, AbilityDefinition>();
            /// Dictionary<string, AbilityDefinition> runtimeDictionary =
            ///     new Dictionary<string, AbilityDefinition>(seed);
            /// ]]></code>
            /// </example>
            public Dictionary(IDictionary<TKey, TValue> dictionary)
                : base(dictionary) { }

            /// <summary>
            /// Rehydrates the dictionary from a <see cref="SerializationInfo"/> payload.
            /// </summary>
            /// <param name="serializationInfo">Serialized data describing the dictionary.</param>
            /// <param name="streamingContext">Context about the serialization source or destination.</param>
            /// <example>
            /// <code><![CDATA[
            /// SerializationInfo info = new SerializationInfo(typeof(Dictionary<string, AbilityDefinition>), new FormatterConverter());
            /// StreamingContext context = new StreamingContext(StreamingContextStates.File);
            /// Dictionary<string, AbilityDefinition> runtimeDictionary = new Dictionary<string, AbilityDefinition>(info, context);
            /// ]]></code>
            /// </example>
            public Dictionary(
                SerializationInfo serializationInfo,
                StreamingContext streamingContext
            )
                : base(serializationInfo, streamingContext) { }
        }

        [Serializable]
        public abstract class Cache { }

        /// <summary>
        /// Produces a JSON string that mirrors the serialized key and value arrays, which is useful for debugging.
        /// </summary>
        /// <returns>A JSON representation of the dictionary contents.</returns>
        /// <example>
        /// <code><![CDATA[
        /// AbilityDictionary abilityLookup = new AbilityDictionary();
        /// abilityLookup["Dash"] = dashDefinition;
        /// string preview = abilityLookup.ToString();
        /// Debug.Log(preview);
        /// ]]></code>
        /// </example>
        public override string ToString()
        {
            return this.ToJson();
        }

        internal abstract void EditorAfterDeserialize();

        /// <summary>
        /// Syncs the runtime dictionary state to the serialized arrays (_keys and _values).
        /// This is the inverse of EditorAfterDeserialize - it writes runtime state to serialized state.
        /// Used by editor code when directly modifying the dictionary and needing to persist changes.
        /// </summary>
        internal abstract void EditorSyncSerializedArrays();
    }

    /// <summary>
    /// Shared implementation for Unity serializable dictionaries that keeps serialized key/value arrays synchronized with the runtime <see cref="Dictionary{TKey,TValue}"/>.
    /// Override <see cref="SetValue"/> and <see cref="GetValue"/> to control how values move between serialized caches and the live map.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// [Serializable]
    /// public sealed class AbilityDefinition : ScriptableObject
    /// {
    ///     public string Id;
    /// }
    ///
    /// [Serializable]
    /// public sealed class AbilityCache : SerializableDictionary.Cache<AbilityDefinition>
    /// {
    /// }
    ///
    /// [Serializable]
    /// public sealed class AbilityDictionary
    ///     : SerializableDictionaryBase<string, AbilityDefinition, AbilityCache>
    /// {
    ///     public AbilityDictionary()
    ///         : base(new Dictionary<string, AbilityDefinition>(StringComparer.OrdinalIgnoreCase))
    ///     {
    ///     }
    ///
    ///     protected override AbilityDefinition GetValue(AbilityCache[] cache, int index)
    ///     {
    ///         return cache[index].Data;
    ///     }
    ///
    ///     protected override void SetValue(AbilityCache[] cache, int index, AbilityDefinition value)
    ///     {
    ///         cache[index] = new AbilityCache { Data = value };
    ///     }
    /// }
    /// ]]></code>
    /// </example>
    /// <typeparam name="TKey">Dictionary key type.</typeparam>
    /// <typeparam name="TValue">Dictionary value type.</typeparam>
    /// <typeparam name="TValueCache">Serialized value cache type.</typeparam>
    [Serializable]
    [ProtoContract(IgnoreListHandling = true)]
    public abstract class SerializableDictionaryBase<TKey, TValue, TValueCache>
        : SerializableDictionaryBase,
            IDictionary<TKey, TValue>,
            IDictionary,
            ISerializationCallbackReceiver,
            IDeserializationCallback,
            ISerializable,
            IReadOnlyDictionary<TKey, TValue>
    {
        [ProtoIgnore]
        [JsonIgnore]
        protected internal Dictionary<TKey, TValue> _dictionary;

        [SerializeField]
        [ProtoMember(1, OverwriteList = true)]
        protected internal TKey[] _keys;

        [SerializeField]
        [ProtoMember(2, OverwriteList = true)]
        protected internal TValueCache[] _values;

        [NonSerialized]
        protected internal bool _preserveSerializedEntries;

        [NonSerialized]
        protected internal bool _hasDuplicatesOrNulls;

        /// <summary>
        /// Tracks keys added since the last serialization cycle, in insertion order.
        /// This is used to preserve the order in which entries were added during the next serialization.
        /// </summary>
        [NonSerialized]
        protected internal List<TKey> _newKeysOrder;

        protected internal bool PreserveSerializedEntries => _preserveSerializedEntries;

        protected internal bool HasDuplicatesOrNulls => _hasDuplicatesOrNulls;

        protected internal TKey[] SerializedKeys => _keys;

        protected internal TValueCache[] SerializedValues => _values;

        protected SerializableDictionaryBase()
        {
            _dictionary = new Dictionary<TKey, TValue>();
        }

        protected SerializableDictionaryBase(IDictionary<TKey, TValue> dictionary)
        {
            _dictionary = new Dictionary<TKey, TValue>(dictionary);
        }

        protected SerializableDictionaryBase(
            SerializationInfo serializationInfo,
            StreamingContext streamingContext
        )
        {
            _dictionary = new Dictionary<TKey, TValue>(serializationInfo, streamingContext);
        }

        internal static class SerializedPropertyNames
        {
            private sealed class NameHolder : SerializableDictionary<TKey, TValue>
            {
                public const string KeysName = nameof(_keys);
                public const string ValuesName = nameof(_values);
            }

            internal const string KeysNameInternal = NameHolder.KeysName;
            internal const string ValuesNameInternal = NameHolder.ValuesName;
        }

        /// <summary>
        /// Rebuilds the runtime dictionary from the serialized key/value arrays after Unity or ProtoBuf deserialization.
        /// </summary>
        /// <remarks>
        /// Invoked automatically by Unity; call it manually only when deserializing outside of Unity's pipeline.
        /// </remarks>
        /// <example>
        /// <code><![CDATA[
        /// dictionary.OnAfterDeserialize();
        /// IReadOnlyDictionary<TKey, TValue> restored = dictionary;
        /// ]]></code>
        /// </example>
        public void OnAfterDeserialize()
        {
            OnAfterDeserializeInternal(suppressWarnings: false);
        }

        internal override void EditorAfterDeserialize()
        {
            OnAfterDeserializeInternal(suppressWarnings: true);
        }

        internal override void EditorSyncSerializedArrays()
        {
            // Force sync from runtime dictionary to serialized arrays
            _preserveSerializedEntries = false;
            OnBeforeSerialize();
        }

        private void OnAfterDeserializeInternal(bool suppressWarnings)
        {
            bool keysAndValuesPresent =
                _keys != null && _values != null && _keys.Length == _values.Length;

            if (!keysAndValuesPresent)
            {
                _keys = null;
                _values = null;
                _preserveSerializedEntries = false;
                _hasDuplicatesOrNulls = false;
                return;
            }

            _dictionary.Clear();
            HashSet<TKey> observedKeys = new();
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
                    if (!suppressWarnings)
                    {
                        LogNullReferenceSkip("key", index);
                    }
                    continue;
                }

                if (!hasDuplicateKeys && !observedKeys.Add(key))
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
                $"SerializableDictionary<{typeof(TKey).FullName}, {typeof(TValue).FullName}> skipped serialized entry at index {index} because the {component} reference was null."
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

        /// <summary>
        /// Packs the runtime dictionary contents into the serialized key/value arrays prior to Unity or ProtoBuf serialization.
        /// </summary>
        /// <remarks>
        /// <para>
        /// When a serialized array already exists from a previous deserialization, this method preserves its
        /// order while synchronizing with the runtime dictionary. This ensures that the user-defined order of elements
        /// as shown in the Unity inspector is maintained across domain reloads and serialization cycles.
        /// </para>
        /// <para>
        /// The synchronization process:
        /// <list type="bullet">
        /// <item><description>Existing entries: Kept in their original order if still in the dictionary</description></item>
        /// <item><description>Removed entries: Filtered out from the arrays</description></item>
        /// <item><description>New entries: Appended to the end of the arrays in insertion order</description></item>
        /// </list>
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// dictionary.OnBeforeSerialize();
        /// var keys = dictionary.SerializedKeys;
        /// </code>
        /// </example>
        public void OnBeforeSerialize()
        {
            bool arraysIntact = _keys != null && _values != null && _keys.Length == _values.Length;

            // If we have valid arrays with duplicates/nulls and should preserve them,
            // skip sync entirely to maintain the inspector's view of problematic data.
            if (_preserveSerializedEntries && arraysIntact && _hasDuplicatesOrNulls)
            {
                return;
            }

            // If we have valid arrays and should preserve order, sync while maintaining order
            if (_preserveSerializedEntries && arraysIntact)
            {
                SyncSerializedArraysPreservingOrder();
                return;
            }

            // If arrays exist but are not being preserved (dirty), try to preserve order
            if (arraysIntact)
            {
                SyncSerializedArraysPreservingOrder();
                _preserveSerializedEntries = true;
                return;
            }

            // No existing arrays - build from scratch (dictionary's natural iteration order)
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
            _newKeysOrder?.Clear();
        }

        /// <summary>
        /// Synchronizes the serialized arrays with the dictionary while preserving the existing order.
        /// Existing keys are kept in their original positions, removed keys are filtered out,
        /// and new keys are appended in insertion order.
        /// </summary>
        private void SyncSerializedArraysPreservingOrder()
        {
            int dictCount = _dictionary.Count;
            int arrayLength = _keys.Length;

            // Fast path: if counts match, all array keys are unique, and all keys still exist in the dictionary, no changes needed.
            // We must check for uniqueness because duplicate keys in the array can make counts match by coincidence
            // (e.g., array has {3, 3} with dictCount=2 after adding key 4, but the array should become {3, 4}).
            if (dictCount == arrayLength)
            {
                using PooledResource<HashSet<TKey>> fastPathSeenResource =
                    Buffers<TKey>.HashSet.Get(out HashSet<TKey> fastPathSeenKeys);

                bool allEntriesMatchAndUnique = true;
                for (int i = 0; i < arrayLength; i++)
                {
                    TKey key = _keys[i];
                    // Check both that the key exists in the dictionary AND that it's not a duplicate in the array
                    if (
                        !_dictionary.TryGetValue(key, out TValue dictValue)
                        || !fastPathSeenKeys.Add(key)
                    )
                    {
                        allEntriesMatchAndUnique = false;
                        break;
                    }
                }

                if (allEntriesMatchAndUnique)
                {
                    // Update values in case they changed, but keep order
                    for (int i = 0; i < arrayLength; i++)
                    {
                        TKey key = _keys[i];
                        SetValue(_values, i, _dictionary[key]);
                    }

                    _newKeysOrder?.Clear();
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
                if (_dictionary.TryGetValue(key, out TValue value) && seenKeys.Add(key))
                {
                    newKeys.Add(key);
                    newValues.Add(value);
                }
            }

            // Second pass: append new keys in the order they were added (if tracked)
            if (_newKeysOrder is { Count: > 0 })
            {
                foreach (TKey key in _newKeysOrder)
                {
                    // Only add if it still exists in the dictionary and wasn't already seen
                    if (_dictionary.TryGetValue(key, out TValue value) && seenKeys.Add(key))
                    {
                        newKeys.Add(key);
                        newValues.Add(value);
                    }
                }
            }
            else
            {
                // Fallback: iterate over the dictionary for keys not in the original array
                // (order may not match insertion order)
                foreach (KeyValuePair<TKey, TValue> pair in _dictionary)
                {
                    if (seenKeys.Add(pair.Key))
                    {
                        newKeys.Add(pair.Key);
                        newValues.Add(pair.Value);
                    }
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

            // Clear the tracked new keys since they're now in the serialized arrays
            _newKeysOrder?.Clear();
        }

        /// <summary>
        /// Tracks a newly added key for order preservation during serialization.
        /// </summary>
        private void TrackNewKey(TKey key)
        {
            _newKeysOrder ??= new List<TKey>();
            _newKeysOrder.Add(key);
        }

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
            OnAfterDeserializeInternal(suppressWarnings: false);
        }

        protected abstract void SetValue(TValueCache[] cache, int index, TValue value);

        protected abstract TValue GetValue(TValueCache[] cache, int index);

        /// <summary>
        /// Replaces this dictionary's contents with another dictionary.
        /// </summary>
        /// <param name="dictionary">Source dictionary.</param>
        public void CopyFrom(IDictionary<TKey, TValue> dictionary)
        {
            _dictionary ??= new Dictionary<TKey, TValue>();
            _dictionary.Clear();
            foreach (KeyValuePair<TKey, TValue> pair in dictionary)
            {
                _dictionary[pair.Key] = pair.Value;
            }

            // Clear the order tracking since we're replacing all content
            _newKeysOrder?.Clear();
            _keys = null;
            _values = null;
            MarkSerializationCacheDirty();
        }

        /// <summary>
        /// Creates a new <see cref="global::System.Collections.Generic.Dictionary{TKey, TValue}"/> populated with this dictionary's contents.
        /// </summary>
        /// <returns>A copy of the dictionary's current state.</returns>
        public global::System.Collections.Generic.Dictionary<TKey, TValue> ToDictionary()
        {
            global::System.Collections.Generic.Dictionary<TKey, TValue> copy = new(
                _dictionary,
                _dictionary.Comparer
            );
            return copy;
        }

        /// <summary>
        /// Creates a new array containing all keys in the dictionary's natural iteration order.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Returns keys in the order determined by the underlying <see cref="Dictionary{TKey, TValue}"/>'s iteration order.
        /// This matches the behavior of <see cref="Dictionary{TKey, TValue}.Keys"/> and standard dictionary semantics.
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
        /// <returns>A new array containing all keys in dictionary iteration order.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableDictionary<string, int> scores = new SerializableDictionary<string, int>();
        /// scores["Alice"] = 100;
        /// scores["Bob"] = 85;
        /// string[] keyArray = scores.ToKeysArray();
        /// ]]></code>
        /// </example>
        public TKey[] ToKeysArray()
        {
            int count = _dictionary.Count;
            if (count == 0)
            {
                return Array.Empty<TKey>();
            }

            // Return keys in dictionary iteration order (from the underlying Dictionary)
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
        /// To retrieve keys in the dictionary's natural iteration order, use <see cref="ToKeysArray"/> instead.
        /// </para>
        /// </remarks>
        /// <returns>A new array containing all keys in serialization order.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableDictionary<string, int> scores = new SerializableDictionary<string, int>();
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
        /// Creates a new array containing all values in the dictionary's natural iteration order.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Returns values in the order determined by the underlying <see cref="Dictionary{TKey, TValue}"/>'s iteration order.
        /// The value at index <c>i</c> corresponds to the key at index <c>i</c> from <see cref="ToKeysArray"/>.
        /// This matches the behavior of <see cref="Dictionary{TKey, TValue}.Values"/> and standard dictionary semantics.
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
        /// <returns>A new array containing all values in dictionary iteration order.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableDictionary<string, int> scores = new SerializableDictionary<string, int>();
        /// scores["Alice"] = 100;
        /// scores["Bob"] = 85;
        /// int[] valueArray = scores.ToValuesArray();
        /// ]]></code>
        /// </example>
        public TValue[] ToValuesArray()
        {
            int count = _dictionary.Count;
            if (count == 0)
            {
                return Array.Empty<TValue>();
            }

            // Return values in dictionary iteration order (from the underlying Dictionary)
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
        /// To retrieve values in the dictionary's natural iteration order, use <see cref="ToValuesArray"/> instead.
        /// </para>
        /// </remarks>
        /// <returns>A new array containing all values in serialization order.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableDictionary<string, int> scores = new SerializableDictionary<string, int>();
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
        /// Creates a new array containing all key-value pairs in the dictionary's natural iteration order.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Returns pairs in the order determined by the underlying <see cref="Dictionary{TKey, TValue}"/>'s iteration order.
        /// This matches the behavior of enumerating a <see cref="Dictionary{TKey, TValue}"/> and standard dictionary semantics.
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
        /// <returns>A new array containing all key-value pairs in dictionary iteration order.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableDictionary<string, int> scores = new SerializableDictionary<string, int>();
        /// scores["Alice"] = 100;
        /// scores["Bob"] = 85;
        /// KeyValuePair<string, int>[] pairArray = scores.ToArray();
        /// ]]></code>
        /// </example>
        public KeyValuePair<TKey, TValue>[] ToArray()
        {
            int count = _dictionary.Count;
            if (count == 0)
            {
                return Array.Empty<KeyValuePair<TKey, TValue>>();
            }

            // Return pairs in dictionary iteration order (from the underlying Dictionary)
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
        /// To retrieve key-value pairs in the dictionary's natural iteration order, use <see cref="ToArray"/> instead.
        /// </para>
        /// </remarks>
        /// <returns>A new array containing all key-value pairs in serialization order.</returns>
        /// <example>
        /// <code><![CDATA[
        /// SerializableDictionary<string, int> scores = new SerializableDictionary<string, int>();
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

        private void MarkSerializationCacheDirty()
        {
            _preserveSerializedEntries = false;
            _hasDuplicatesOrNulls = false;
            // Note: We intentionally do NOT null out _keys and _values here.
            // They are preserved so that SyncSerializedArraysPreservingOrder can maintain
            // the existing order of keys while applying mutations.
        }

        public ICollection<TKey> Keys => _dictionary.Keys;
        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;

        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;

        public ICollection<TValue> Values => _dictionary.Values;

        public int Count => _dictionary.Count;

        public bool IsReadOnly => ((IDictionary<TKey, TValue>)_dictionary).IsReadOnly;

        /// <summary>
        /// Gets or sets a value associated with the provided key in the runtime dictionary.
        /// Updates invalidate the serialized cache so Unity and ProtoBuf can pick up the changes.
        /// </summary>
        /// <param name="key">The key of the entry to access.</param>
        /// <returns>The stored value.</returns>
        /// <example>
        /// <code><![CDATA[
        /// AbilityDictionary abilityLookup = new AbilityDictionary();
        /// abilityLookup["Dash"] = dashDefinition;
        /// AbilityDefinition dash = abilityLookup["Dash"];
        /// ]]></code>
        /// </example>
        public TValue this[TKey key]
        {
            get => _dictionary[key];
            set
            {
                bool isNewKey = !_dictionary.ContainsKey(key);
                _dictionary[key] = value;
                if (isNewKey)
                {
                    TrackNewKey(key);
                }
                MarkSerializationCacheDirty();
            }
        }

        /// <summary>
        /// Adds a new key/value pair to the runtime dictionary and marks the serialized cache as dirty so Unity can persist the change.
        /// </summary>
        /// <param name="key">The key to insert.</param>
        /// <param name="value">The value associated with the key.</param>
        /// <example>
        /// <code><![CDATA[
        /// AbilityDictionary abilities = new AbilityDictionary();
        /// abilities.Add("Dash", dashDefinition);
        /// ]]></code>
        /// </example>
        public void Add(TKey key, TValue value)
        {
            _dictionary.Add(key, value);
            TrackNewKey(key);
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
        /// AbilityDictionary abilities = new AbilityDictionary();
        /// bool added = abilities.TryAdd("Dash", dashDefinition);
        /// ]]></code>
        /// </example>
        public bool TryAdd(TKey key, TValue value)
        {
            bool added = _dictionary.TryAdd(key, value);
            if (added)
            {
                TrackNewKey(key);
                MarkSerializationCacheDirty();
            }

            return added;
        }

        /// <summary>
        /// Determines whether the runtime dictionary already contains the specified key.
        /// </summary>
        /// <param name="key">The key to locate.</param>
        /// <returns><c>true</c> when the key exists.</returns>
        /// <example>
        /// <code><![CDATA[
        /// AbilityDictionary abilities = new AbilityDictionary();
        /// bool hasDash = abilities.ContainsKey("Dash");
        /// ]]></code>
        /// </example>
        public bool ContainsKey(TKey key)
        {
            return _dictionary.ContainsKey(key);
        }

        /// <summary>
        /// Removes an entry by key and invalidates the serialized cache if the key existed.
        /// </summary>
        /// <param name="key">The key to remove.</param>
        /// <returns><c>true</c> when an entry was removed.</returns>
        /// <example>
        /// <code><![CDATA[
        /// AbilityDictionary abilities = new AbilityDictionary();
        /// bool removed = abilities.Remove("Dash");
        /// ]]></code>
        /// </example>
        public bool Remove(TKey key)
        {
            bool removed = _dictionary.Remove(key);
            if (removed)
            {
                // Remove from tracked new keys if present
                _newKeysOrder?.Remove(key);
                MarkSerializationCacheDirty();
            }

            return removed;
        }

        /// <summary>
        /// Removes an entry and outputs the value that was previously stored.
        /// </summary>
        /// <param name="key">The key to remove.</param>
        /// <param name="value">Receives the removed value when successful.</param>
        /// <returns><c>true</c> when the key was found.</returns>
        /// <example>
        /// <code><![CDATA[
        /// AbilityDictionary abilities = new AbilityDictionary();
        /// AbilityDefinition removed;
        /// bool removedEntry = abilities.Remove("Dash", out removed);
        /// ]]></code>
        /// </example>
        public bool Remove(TKey key, out TValue value)
        {
            bool removed = _dictionary.Remove(key, out value);
            if (removed)
            {
                // Remove from tracked new keys if present
                _newKeysOrder?.Remove(key);
                MarkSerializationCacheDirty();
            }

            return removed;
        }

        /// <summary>
        /// Retrieves a value without throwing when the key is missing.
        /// </summary>
        /// <param name="key">The key to locate.</param>
        /// <param name="value">Outputs the located value.</param>
        /// <returns><c>true</c> when the key exists.</returns>
        /// <example>
        /// <code><![CDATA[
        /// AbilityDictionary abilities = new AbilityDictionary();
        /// AbilityDefinition dash;
        /// if (abilities.TryGetValue("Dash", out dash))
        /// {
        ///     dash.Activate();
        /// }
        /// ]]></code>
        /// </example>
        public bool TryGetValue(TKey key, out TValue value)
        {
            return _dictionary.TryGetValue(key, out value);
        }

        /// <summary>
        /// Adds a <see cref="KeyValuePair{TKey, TValue}"/> to the dictionary via the <see cref="ICollection{T}"/> interface.
        /// </summary>
        /// <param name="item">The entry to add.</param>
        /// <example>
        /// <code><![CDATA[
        /// AbilityDictionary abilities = new AbilityDictionary();
        /// KeyValuePair<string, AbilityDefinition> entry = new KeyValuePair<string, AbilityDefinition>("Dash", dashDefinition);
        /// abilities.Add(entry);
        /// ]]></code>
        /// </example>
        public void Add(KeyValuePair<TKey, TValue> item)
        {
            ((IDictionary<TKey, TValue>)_dictionary).Add(item);
            TrackNewKey(item.Key);
            MarkSerializationCacheDirty();
        }

        /// <summary>
        /// Removes all entries from the dictionary and clears the serialized arrays.
        /// </summary>
        /// <example>
        /// <code><![CDATA[
        /// AbilityDictionary abilities = new AbilityDictionary();
        /// abilities.Clear();
        /// ]]></code>
        /// </example>
        public void Clear()
        {
            _dictionary.Clear();
            _newKeysOrder?.Clear();
            // Clear the arrays completely since we're removing all entries
            _keys = null;
            _values = null;
            MarkSerializationCacheDirty();
        }

        /// <summary>
        /// Determines whether the dictionary contains the provided key/value pair.
        /// </summary>
        /// <param name="item">The entry to look for.</param>
        /// <returns><c>true</c> when both the key and value match.</returns>
        /// <example>
        /// <code><![CDATA[
        /// AbilityDictionary abilities = new AbilityDictionary();
        /// KeyValuePair<string, AbilityDefinition> entry = new KeyValuePair<string, AbilityDefinition>("Dash", dashDefinition);
        /// bool present = abilities.Contains(entry);
        /// ]]></code>
        /// </example>
        public bool Contains(KeyValuePair<TKey, TValue> item)
        {
            return ((IDictionary<TKey, TValue>)_dictionary).Contains(item);
        }

        /// <summary>
        /// Copies the contents of the dictionary into the provided array, which is useful when interoperating with legacy APIs.
        /// </summary>
        /// <param name="array">The destination array.</param>
        /// <param name="arrayIndex">The index to start copying into.</param>
        /// <example>
        /// <code><![CDATA[
        /// AbilityDictionary abilities = new AbilityDictionary();
        /// KeyValuePair<string, AbilityDefinition>[] snapshot = new KeyValuePair<string, AbilityDefinition>[abilities.Count];
        /// abilities.CopyTo(snapshot, 0);
        /// ]]></code>
        /// </example>
        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            ((IDictionary<TKey, TValue>)_dictionary).CopyTo(array, arrayIndex);
        }

        /// <summary>
        /// Removes the specified key/value pair only when both components match the stored entry.
        /// </summary>
        /// <param name="item">The entry to remove.</param>
        /// <returns><c>true</c> when the pair existed.</returns>
        /// <example>
        /// <code><![CDATA[
        /// AbilityDictionary abilities = new AbilityDictionary();
        /// KeyValuePair<string, AbilityDefinition> entry = new KeyValuePair<string, AbilityDefinition>("Dash", dashDefinition);
        /// bool removed = abilities.Remove(entry);
        /// ]]></code>
        /// </example>
        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            bool removed = ((IDictionary<TKey, TValue>)_dictionary).Remove(item);
            if (removed)
            {
                // Remove from tracked new keys if present
                _newKeysOrder?.Remove(item.Key);
                MarkSerializationCacheDirty();
            }

            return removed;
        }

        /// <summary>
        /// Returns a struct enumerator that iterates over key/value pairs without allocations.
        /// </summary>
        /// <returns>An enumerator positioned before the first entry.</returns>
        /// <example>
        /// <code><![CDATA[
        /// AbilityDictionary abilities = new AbilityDictionary();
        /// foreach (KeyValuePair<string, AbilityDefinition> entry in abilities)
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
        /// Indicates whether the non-generic <see cref="IDictionary"/> wrapper has a fixed size (it does not).
        /// </summary>
        public bool IsFixedSize => ((IDictionary)_dictionary).IsFixedSize;

        ICollection IDictionary.Keys => _dictionary.Keys;

        ICollection IDictionary.Values => _dictionary.Values;

        /// <summary>
        /// Indicates whether access to the dictionary is synchronized (thread-safe).
        /// </summary>
        public bool IsSynchronized => ((IDictionary)_dictionary).IsSynchronized;

        /// <summary>
        /// Provides an object that can be used to synchronize access when required by legacy APIs.
        /// </summary>
        public object SyncRoot => ((IDictionary)_dictionary).SyncRoot;

        /// <summary>
        /// Gets or sets entries through the non-generic <see cref="IDictionary"/> interface.
        /// </summary>
        /// <param name="key">The boxed key.</param>
        /// <returns>The boxed value associated with the key.</returns>
        public object this[object key]
        {
            get => ((IDictionary)_dictionary)[key];
            set
            {
                bool isNewKey = !((IDictionary)_dictionary).Contains(key);
                ((IDictionary)_dictionary)[key] = value;
                if (isNewKey && key is TKey typedKey)
                {
                    TrackNewKey(typedKey);
                }
                MarkSerializationCacheDirty();
            }
        }

        /// <summary>
        /// Adds a boxed key/value pair through the non-generic interface.
        /// </summary>
        /// <param name="key">The boxed key.</param>
        /// <param name="value">The boxed value.</param>
        /// <example>
        /// <code><![CDATA[
        /// AbilityDictionary abilities = new AbilityDictionary();
        /// IDictionary boxed = abilities;
        /// boxed.Add((object)"Dash", dashDefinition);
        /// ]]></code>
        /// </example>
        public void Add(object key, object value)
        {
            ((IDictionary)_dictionary).Add(key, value);
            if (key is TKey typedKey)
            {
                TrackNewKey(typedKey);
            }
            MarkSerializationCacheDirty();
        }

        /// <summary>
        /// Determines whether the dictionary contains the provided boxed key.
        /// </summary>
        /// <param name="key">The key to locate.</param>
        /// <returns><c>true</c> when the key exists.</returns>
        /// <example>
        /// <code><![CDATA[
        /// AbilityDictionary abilities = new AbilityDictionary();
        /// IDictionary boxed = abilities;
        /// bool hasDash = boxed.Contains((object)"Dash");
        /// ]]></code>
        /// </example>
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
        /// Removes a boxed entry and marks the serialized cache as dirty when something is deleted.
        /// </summary>
        /// <param name="key">The boxed key to remove.</param>
        /// <example>
        /// <code><![CDATA[
        /// AbilityDictionary abilities = new AbilityDictionary();
        /// IDictionary boxed = abilities;
        /// boxed.Remove((object)"Dash");
        /// ]]></code>
        /// </example>
        public void Remove(object key)
        {
            IDictionary dictionary = _dictionary;
            bool existed = dictionary.Contains(key);
            dictionary.Remove(key);
            if (existed)
            {
                // Remove from tracked new keys if present
                if (key is TKey typedKey)
                {
                    _newKeysOrder?.Remove(typedKey);
                }
                MarkSerializationCacheDirty();
            }
        }

        /// <summary>
        /// Copies the dictionary contents into a non-generic array, matching <see cref="IDictionary.CopyTo"/>.
        /// </summary>
        /// <param name="array">The destination array.</param>
        /// <param name="index">The starting index inside <paramref name="array"/>.</param>
        /// <example>
        /// <code><![CDATA[
        /// AbilityDictionary abilities = new AbilityDictionary();
        /// DictionaryEntry[] entries = new DictionaryEntry[abilities.Count];
        /// IDictionary boxed = abilities;
        /// boxed.CopyTo(entries, 0);
        /// ]]></code>
        /// </example>
        public void CopyTo(Array array, int index)
        {
            ((IDictionary)_dictionary).CopyTo(array, index);
        }

        /// <summary>
        /// Completes deserialization after <see cref="SerializationInfo"/> data has been applied.
        /// </summary>
        /// <param name="sender">Reserved for future use.</param>
        public void OnDeserialization(object sender)
        {
            ((IDeserializationCallback)_dictionary).OnDeserialization(sender);
        }

        /// <summary>
        /// Writes the serialized representation of the dictionary into a <see cref="SerializationInfo"/> instance.
        /// </summary>
        /// <param name="info">The serialization store to populate.</param>
        /// <param name="context">Context for the serialization process.</param>
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            _dictionary.GetObjectData(info, context);
        }

        /// <summary>
        /// Allocation-free enumerator used by <see cref="SerializableDictionaryBase{TKey, TValue, TValueCache}"/>.
        /// </summary>
        /// <example>
        /// <code><![CDATA[
        /// AbilityDictionary abilities = new AbilityDictionary();
        /// SerializableDictionary<string, AbilityDefinition>.Enumerator enumerator = abilities.GetEnumerator();
        /// while (enumerator.MoveNext())
        /// {
        ///     KeyValuePair<string, AbilityDefinition> entry = enumerator.Current;
        /// }
        /// ]]></code>
        /// </example>
        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
        {
            private Dictionary<TKey, TValue>.Enumerator _enumerator;

            internal Enumerator(Dictionary<TKey, TValue>.Enumerator enumerator)
            {
                _enumerator = enumerator;
            }

            public KeyValuePair<TKey, TValue> Current => _enumerator.Current;

            object IEnumerator.Current => _enumerator.Current;

            /// <summary>
            /// Advances the enumerator to the next entry.
            /// </summary>
            /// <returns><c>true</c> when another item is available.</returns>
            public bool MoveNext()
            {
                return _enumerator.MoveNext();
            }

            /// <summary>
            /// Disposes of the underlying dictionary enumerator.
            /// </summary>
            public void Dispose()
            {
                _enumerator.Dispose();
            }

            /// <summary>
            /// Reset is not supported because Unity serializable dictionaries mirror <see cref="System.Collections.Generic.Dictionary{TKey, TValue}"/> semantics.
            /// </summary>
            void IEnumerator.Reset()
            {
                throw new NotSupportedException("Reset is not supported.");
            }
        }
    }

    /// <summary>
    /// Factory and cache helpers for serializable dictionaries.
    /// </summary>
    public static class SerializableDictionary
    {
        [Serializable]
        [ProtoContract]
        public class Cache<T> : SerializableDictionaryBase.Cache
        {
            [ProtoMember(1)]
            public T Data;
        }
    }

    /// <summary>
    /// Unity-friendly dictionary that keeps keys and values serialized for editor, ProtoBuf, and JSON pipelines.
    /// Use this when you need deterministic order, inspector editing, and runtime dictionary semantics without custom wrappers.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// [Serializable]
    /// public sealed class LootTable : MonoBehaviour
    /// {
    ///     [SerializeField]
    ///     private SerializableDictionary<string, int> weights = new SerializableDictionary<string, int>();
    ///
    ///     private void Awake()
    ///     {
    ///         weights["Common"] = 80;
    ///         weights["Rare"] = 15;
    ///         weights["Legendary"] = 5;
    ///     }
    /// }
    /// ]]></code>
    /// </example>
    /// <typeparam name="TKey">Dictionary key type.</typeparam>
    /// <typeparam name="TValue">Dictionary value type.</typeparam>
    [Serializable]
    public class SerializableDictionary<TKey, TValue>
        : SerializableDictionaryBase<TKey, TValue, TValue>
    {
        /// <summary>
        /// Initializes an empty serializable dictionary whose values can be written directly to Unity serialization.
        /// </summary>
        /// <example>
        /// <code><![CDATA[
        /// SerializableDictionary<string, int> weights = new SerializableDictionary<string, int>();
        /// weights["Common"] = 42;
        /// ]]></code>
        /// </example>
        public SerializableDictionary() { }

        /// <summary>
        /// Initializes the serializable dictionary with items copied from an existing dictionary.
        /// </summary>
        /// <param name="dictionary">The source entries to clone.</param>
        /// <example>
        /// <code><![CDATA[
        /// Dictionary<string, int> seed = new Dictionary<string, int>();
        /// seed["Common"] = 42;
        /// SerializableDictionary<string, int> weights = new SerializableDictionary<string, int>(seed);
        /// ]]></code>
        /// </example>
        public SerializableDictionary(IDictionary<TKey, TValue> dictionary)
            : base(dictionary) { }

        /// <summary>
        /// Deserialization constructor used by <see cref="ISerializable"/> pipelines.
        /// </summary>
        /// <param name="serializationInfo">Serialized key/value data.</param>
        /// <param name="streamingContext">Information about the serialization source.</param>
        protected SerializableDictionary(
            SerializationInfo serializationInfo,
            StreamingContext streamingContext
        )
            : base(serializationInfo, streamingContext) { }

        protected override TValue GetValue(TValue[] cache, int index)
        {
            return cache[index];
        }

        protected override void SetValue(TValue[] cache, int index, TValue value)
        {
            cache[index] = value;
        }
    }

    internal static class SerializableDictionarySerializedPropertyNames
    {
        internal const string Keys = SerializableDictionary<int, int>
            .SerializedPropertyNames
            .KeysNameInternal;

        internal const string Values = SerializableDictionary<int, int>
            .SerializedPropertyNames
            .ValuesNameInternal;
    }

    /// <summary>
    /// Serializable dictionary that stores value data inside cache objects so complex or non-serializable runtime types can participate in Unity serialization.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// [Serializable]
    /// [Serializable]
    /// public sealed class ComplexValue
    /// {
    ///     public int Score;
    /// }
    ///
    /// [Serializable]
    /// public sealed class ComplexValueCache : SerializableDictionary.Cache<ComplexValue>
    /// {
    /// }
    ///
    /// [Serializable]
    /// public sealed class ComplexValueDictionary
    ///     : SerializableDictionary<string, ComplexValue, ComplexValueCache>
    /// {
    /// }
    /// ]]></code>
    /// </example>
    /// <typeparam name="TKey">Dictionary key type.</typeparam>
    /// <typeparam name="TValue">Dictionary value type.</typeparam>
    /// <typeparam name="TValueCache">Serialized value cache type.</typeparam>
    [Serializable]
    public class SerializableDictionary<TKey, TValue, TValueCache>
        : SerializableDictionaryBase<TKey, TValue, TValueCache>
        where TValueCache : SerializableDictionary.Cache<TValue>, new()
    {
        /// <summary>
        /// Initializes an empty serializable dictionary whose values are stored in cache objects.
        /// </summary>
        /// <example>
        /// <code><![CDATA[
        /// SerializableDictionary<string, ComplexValue, ComplexValueCache> cache =
        ///     new SerializableDictionary<string, ComplexValue, ComplexValueCache>();
        /// ]]></code>
        /// </example>
        public SerializableDictionary() { }

        /// <summary>
        /// Initializes the dictionary with entries copied from an existing runtime dictionary.
        /// </summary>
        /// <param name="dictionary">Entries to seed the serializable dictionary with.</param>
        /// <example>
        /// <code><![CDATA[
        /// Dictionary<string, ComplexValue> seed = new Dictionary<string, ComplexValue>();
        /// SerializableDictionary<string, ComplexValue, ComplexValueCache> cache =
        ///     new SerializableDictionary<string, ComplexValue, ComplexValueCache>(seed);
        /// ]]></code>
        /// </example>
        public SerializableDictionary(IDictionary<TKey, TValue> dictionary)
            : base(dictionary) { }

        /// <summary>
        /// Deserialization constructor required by the <see cref="ISerializable"/> contract.
        /// </summary>
        /// <param name="serializationInfo">Serialized representation of the dictionary.</param>
        /// <param name="streamingContext">Context describing the serialization environment.</param>
        protected SerializableDictionary(
            SerializationInfo serializationInfo,
            StreamingContext streamingContext
        )
            : base(serializationInfo, streamingContext) { }

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
