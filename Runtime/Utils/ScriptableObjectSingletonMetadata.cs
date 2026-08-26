// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.Serialization;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Non-generic helper for ScriptableObjectSingleton initialization state tracking.
    /// </summary>
    internal static class ScriptableObjectSingletonInitState
    {
#if UNITY_EDITOR
        private static bool _initialEnsureCompleted;

        /// <summary>
        /// Indicates whether the initial singleton asset creation pass has completed globally.
        /// When false, metadata-related warnings are suppressed since assets may not exist yet.
        /// </summary>
        internal static bool InitialEnsureCompleted
        {
            get => _initialEnsureCompleted;
            set => _initialEnsureCompleted = value;
        }
#endif
    }

    [Serializable]
    internal sealed class ScriptableObjectSingletonMetadata : ScriptableObject
    {
        public const string ResourcePath =
            "Wallstop Studios/Unity Helpers/ScriptableObjectSingletonMetadata";
        public const string AssetPath =
            "Assets/Resources/Wallstop Studios/Unity Helpers/ScriptableObjectSingletonMetadata.asset";

        /// <summary>
        /// Legacy path for migration from older versions.
        /// </summary>
        internal const string LegacyAssetPath =
            "Assets/Resources/ScriptableObjectSingletonMetadata.asset";

        [FormerlySerializedAs("entries")]
        [SerializeField]
        private List<Entry> _entries = new();

        public bool TryGetEntry(Type type, out Entry entry)
        {
            if (type is null)
            {
                entry = default;
                return false;
            }

            string key = type.AssemblyQualifiedName;
            if (string.IsNullOrEmpty(key) || _entries == null || _entries.Count == 0)
            {
                entry = default;
                return false;
            }

            foreach (Entry candidate in _entries)
            {
                if (
                    string.Equals(
                        candidate.assemblyQualifiedTypeName,
                        key,
                        StringComparison.Ordinal
                    )
                )
                {
                    entry = candidate;
                    return true;
                }
            }

            entry = default;
            return false;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Adds or updates a singleton metadata entry.
        /// </summary>
        /// <param name="entry">The entry to store.</param>
        /// <returns><c>true</c> when the serialized metadata changed; otherwise, <c>false</c>.</returns>
        public bool SetOrUpdateEntry(Entry entry)
        {
            _entries ??= new List<Entry>();

            string key = entry.assemblyQualifiedTypeName;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (
                    string.Equals(
                        _entries[i].assemblyQualifiedTypeName,
                        key,
                        StringComparison.Ordinal
                    )
                )
                {
                    if (EntriesEqual(_entries[i], entry))
                    {
                        return false;
                    }

                    _entries[i] = entry;
                    return true;
                }
            }

            _entries.Add(entry);
            return true;
        }

        private static bool EntriesEqual(Entry left, Entry right)
        {
            return string.Equals(
                    left.assemblyQualifiedTypeName,
                    right.assemblyQualifiedTypeName,
                    StringComparison.Ordinal
                )
                && string.Equals(
                    left.resourcesLoadPath,
                    right.resourcesLoadPath,
                    StringComparison.Ordinal
                )
                && string.Equals(left.resourcesPath, right.resourcesPath, StringComparison.Ordinal)
                && string.Equals(left.assetGuid, right.assetGuid, StringComparison.Ordinal);
        }

        /// <summary>
        /// Removes an entry for the specified type from the metadata.
        /// </summary>
        /// <param name="assemblyQualifiedTypeName">The assembly-qualified type name to remove.</param>
        /// <returns>True if an entry was removed; false otherwise.</returns>
        public bool RemoveEntry(string assemblyQualifiedTypeName)
        {
            if (string.IsNullOrEmpty(assemblyQualifiedTypeName) || _entries == null)
            {
                return false;
            }

            for (int i = 0; i < _entries.Count; i++)
            {
                if (
                    string.Equals(
                        _entries[i].assemblyQualifiedTypeName,
                        assemblyQualifiedTypeName,
                        StringComparison.Ordinal
                    )
                )
                {
                    _entries.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets all entries in the metadata. For editor tooling use only.
        /// </summary>
        public IReadOnlyList<Entry> GetAllEntries()
        {
            return _entries ?? (IReadOnlyList<Entry>)Array.Empty<Entry>();
        }

        /// <summary>
        /// Clears all metadata entries. Available for manual cleanup operations.
        /// Note: The Sync operation updates entries incrementally and does not use this method.
        /// </summary>
        public void ClearAllEntries()
        {
            _entries?.Clear();
        }

        /// <summary>
        /// Delegate that performs the actual sync operation. Set by the Editor assembly.
        /// </summary>
        internal static Action<ScriptableObjectSingletonMetadata> SyncImplementation { get; set; }

        /// <summary>
        /// Re-scans all assemblies for ScriptableObjectSingleton types and updates their metadata entries.
        /// This cleans up stale entries and adds any missing singleton metadata.
        /// </summary>
        [WButton]
        public void Sync()
        {
            if (SyncImplementation != null)
            {
                SyncImplementation(this);
            }
            else
            {
                UnityEngine.Debug.LogWarning(
                    "ScriptableObjectSingletonMetadata.Sync: No sync implementation registered. "
                        + "This method should only be called from the Unity Editor."
                );
            }
        }
#endif

        [Serializable]
        public struct Entry
        {
            public string assemblyQualifiedTypeName;
            public string resourcesLoadPath;
            public string resourcesPath;

            // ReSharper disable once NotAccessedField.Global
            public string assetGuid;
        }
    }
}
