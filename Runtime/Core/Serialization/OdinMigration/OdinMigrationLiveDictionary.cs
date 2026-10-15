// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.OdinMigration
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using Object = UnityEngine.Object;

    /// <summary>Preserves original live dictionary values without loading or writing an owner.</summary>
    public sealed class OdinMigrationLiveDictionary
    {
        /// <summary>Maximum entries accepted in one live dictionary.</summary>
        public const int MaximumEntries = 4096;

        /// <summary>Maximum delivered object slots accepted in one live dictionary.</summary>
        public const int MaximumReferences = 65536;

        /// <summary>Gets whether the original dictionary was null.</summary>
        public bool IsNull { get; }

        /// <summary>Gets whether the original dictionary uses the default enum comparer.</summary>
        public bool HasDefaultComparer { get; }

        /// <summary>Gets the original keys in enumeration order.</summary>
        public IReadOnlyList<int> Keys { get; }

        /// <summary>Gets immutable object lists, preserving null lists and shared list identity.</summary>
        public IReadOnlyList<IReadOnlyList<Object>> Values { get; }

        /// <summary>Gets original list identities for detecting sharing across separately captured fields.</summary>
        public IReadOnlyList<object> ListIdentities { get; }

        private OdinMigrationLiveDictionary(
            bool isNull,
            bool hasDefaultComparer,
            List<int> keys,
            List<IReadOnlyList<Object>> values,
            List<object> listIdentities
        )
        {
            IsNull = isNull;
            HasDefaultComparer = hasDefaultComparer;
            Keys = keys.AsReadOnly();
            Values = values.AsReadOnly();
            ListIdentities = listIdentities.AsReadOnly();
        }

        /// <summary>Copies a live enum-to-object-list dictionary with bounded delivered values.</summary>
        /// <param name="dictionary">The original live dictionary, including null.</param>
        /// <param name="encodeKey">Converts a compiler-resolved enum key to its Int32 wire value.</param>
        /// <param name="snapshot">The immutable snapshot, or null on failure.</param>
        /// <param name="failure">The unsupported input or capture failure.</param>
        /// <returns>Whether every original entry was captured.</returns>
        public static bool TryCapture<TKey, TObject>(
            Dictionary<TKey, List<TObject>> dictionary,
            Func<TKey, int> encodeKey,
            out OdinMigrationLiveDictionary snapshot,
            out string failure
        )
            where TKey : struct, Enum
            where TObject : Object
        {
            try
            {
                if (encodeKey == null || Enum.GetUnderlyingType(typeof(TKey)) != typeof(int))
                {
                    snapshot = null;
                    failure = "Live dictionary capture requires an Int32 enum key encoder.";
                    return false;
                }
                List<int> keys = new();
                List<IReadOnlyList<Object>> values = new();
                List<object> listIdentities = new();
                Dictionary<List<TObject>, IReadOnlyList<Object>> lists = new(
                    ReferenceIdentityComparer<List<TObject>>.Instance
                );
                HashSet<int> encodedKeys = new();
                int delivered = 0;
                if (dictionary != null)
                {
                    foreach (KeyValuePair<TKey, List<TObject>> entry in dictionary)
                    {
                        if (MaximumEntries <= keys.Count)
                        {
                            snapshot = null;
                            failure = "Live dictionary capture exceeds the entry limit.";
                            return false;
                        }
                        int key = encodeKey(entry.Key);
                        if (!encodedKeys.Add(key))
                        {
                            snapshot = null;
                            failure = "Live dictionary capture has duplicate encoded keys.";
                            return false;
                        }
                        keys.Add(key);
                        listIdentities.Add(entry.Value);
                        if (entry.Value == null)
                        {
                            values.Add(null);
                            continue;
                        }
                        if (!lists.TryGetValue(entry.Value, out IReadOnlyList<Object> captured))
                        {
                            List<Object> objects = new();
                            foreach (TObject value in entry.Value)
                            {
                                if (MaximumReferences <= delivered)
                                {
                                    snapshot = null;
                                    failure =
                                        "Live dictionary capture exceeds the reference limit.";
                                    return false;
                                }
                                ++delivered;
                                objects.Add(value);
                            }
                            captured = objects.AsReadOnly();
                            lists.Add(entry.Value, captured);
                        }
                        else
                        {
                            if (MaximumReferences - delivered < captured.Count)
                            {
                                snapshot = null;
                                failure = "Live dictionary capture exceeds the reference limit.";
                                return false;
                            }
                            delivered += captured.Count;
                        }
                        values.Add(captured);
                    }
                }
                snapshot = new OdinMigrationLiveDictionary(
                    dictionary == null,
                    dictionary == null
                        || ReferenceEquals(dictionary.Comparer, EqualityComparer<TKey>.Default),
                    keys,
                    values,
                    listIdentities
                );
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                snapshot = null;
                failure = exception.Message;
                return false;
            }
        }

        /// <summary>Rebuilds captured values, preserving null lists and list sharing within one dictionary.</summary>
        public static bool TryRebuild<TKey, TObject>(
            IReadOnlyList<int> keys,
            IReadOnlyList<IReadOnlyList<Object>> values,
            Func<int, TKey> decodeKey,
            out Dictionary<TKey, List<TObject>> dictionary,
            out string failure
        )
            where TKey : struct, Enum
            where TObject : Object
        {
            try
            {
                if (
                    keys == null
                    || values == null
                    || decodeKey == null
                    || keys.Count != values.Count
                    || MaximumEntries < keys.Count
                    || Enum.GetUnderlyingType(typeof(TKey)) != typeof(int)
                )
                {
                    dictionary = null;
                    failure =
                        "Original dictionary recovery requires complete bounded Int32 keys and values.";
                    return false;
                }
                Dictionary<TKey, List<TObject>> rebuilt = new();
                Dictionary<IReadOnlyList<Object>, List<TObject>> lists = new(
                    ReferenceIdentityComparer<IReadOnlyList<Object>>.Instance
                );
                int delivered = 0;
                int entryCount = keys.Count;
                for (int index = 0; index < entryCount; ++index)
                {
                    IReadOnlyList<Object> references = values[index];
                    List<TObject> objects = null;
                    if (references != null)
                    {
                        int referenceCount = references.Count;
                        if (referenceCount < 0 || MaximumReferences - delivered < referenceCount)
                        {
                            dictionary = null;
                            failure = "Original dictionary recovery exceeds the reference limit.";
                            return false;
                        }
                        bool shared = lists.TryGetValue(references, out objects);
                        if (shared && objects.Count != referenceCount)
                        {
                            dictionary = null;
                            failure =
                                "Original dictionary recovery observed a changed shared list.";
                            return false;
                        }
                        if (!shared)
                        {
                            objects = new List<TObject>();
                        }
                        for (
                            int referenceIndex = 0;
                            referenceIndex < referenceCount;
                            ++referenceIndex
                        )
                        {
                            Object reference = references[referenceIndex];
                            if (
                                !ReferenceEquals(reference, null)
                                && (reference == null || !(reference is TObject))
                            )
                            {
                                dictionary = null;
                                failure =
                                    "Original dictionary recovery has destroyed or incompatible references.";
                                return false;
                            }
                            if (shared)
                            {
                                if (!ReferenceEquals(objects[referenceIndex], reference))
                                {
                                    dictionary = null;
                                    failure =
                                        "Original dictionary recovery observed a changed shared list.";
                                    return false;
                                }
                            }
                            else
                            {
                                objects.Add((TObject)reference);
                            }
                            ++delivered;
                        }
                        if (!shared)
                        {
                            lists.Add(references, objects);
                        }
                    }
                    if (!rebuilt.TryAdd(decodeKey(keys[index]), objects))
                    {
                        dictionary = null;
                        failure = "Original dictionary recovery has duplicate enum keys.";
                        return false;
                    }
                }
                dictionary = rebuilt;
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                dictionary = null;
                failure = exception.Message;
                return false;
            }
        }

        /// <summary>Restores captured values into an existing dictionary after complete validation.</summary>
        public static bool TryRestoreContents<TKey, TObject>(
            Dictionary<TKey, List<TObject>> dictionary,
            IReadOnlyList<int> keys,
            IReadOnlyList<IReadOnlyList<Object>> values,
            Func<int, TKey> decodeKey,
            out string failure
        )
            where TKey : struct, Enum
            where TObject : Object
        {
            try
            {
                if (
                    dictionary == null
                    || !ReferenceEquals(dictionary.Comparer, EqualityComparer<TKey>.Default)
                )
                {
                    failure =
                        "Dictionary content recovery requires an existing dictionary with the default comparer.";
                    return false;
                }
                if (
                    !TryRebuild(
                        keys,
                        values,
                        decodeKey,
                        out Dictionary<TKey, List<TObject>> rebuilt,
                        out failure
                    )
                )
                {
                    return false;
                }
                dictionary.EnsureCapacity(rebuilt.Count);
                dictionary.Clear();
                foreach (KeyValuePair<TKey, List<TObject>> entry in rebuilt)
                {
                    dictionary.Add(entry.Key, entry.Value);
                }
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                return false;
            }
        }
    }
#endif
}
