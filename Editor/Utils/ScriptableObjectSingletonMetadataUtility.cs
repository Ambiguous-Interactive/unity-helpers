// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    internal static class ScriptableObjectSingletonMetadataUtility
    {
        internal static ScriptableObjectSingletonMetadata LoadOrCreateMetadataAsset()
        {
            // Try loading from current path first
            ScriptableObjectSingletonMetadata metadata =
                AssetDatabase.LoadAssetAtPath<ScriptableObjectSingletonMetadata>(
                    ScriptableObjectSingletonMetadata.AssetPath
                );
            if (metadata != null)
            {
                return metadata;
            }

            // Check for legacy path and migrate if found
            ScriptableObjectSingletonMetadata legacyMetadata =
                AssetDatabase.LoadAssetAtPath<ScriptableObjectSingletonMetadata>(
                    ScriptableObjectSingletonMetadata.LegacyAssetPath
                );
            if (legacyMetadata != null)
            {
                // Skip migration during test runs to avoid Unity's internal modal dialogs
                // unless explicitly allowed
                if (
                    EditorUi.Suppress
                    && !ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression
                )
                {
                    return legacyMetadata;
                }
                return MigrateLegacyMetadata(legacyMetadata);
            }

            // Skip creating new assets during test runs to avoid Unity's internal modal dialogs
            // when asset operations fail, unless explicitly allowed.
            if (
                EditorUi.Suppress
                && !ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression
            )
            {
                return null;
            }

            // Create new asset at current path
            if (!EnsureResourcesFolder())
            {
                Debug.LogWarning(
                    "ScriptableObjectSingletonMetadataUtility: Could not ensure Resources folder exists. Skipping metadata asset creation."
                );
                return null;
            }

            ScriptableObjectSingletonMetadata created =
                ScriptableObject.CreateInstance<ScriptableObjectSingletonMetadata>();

            // If we're inside a batch scope, temporarily exit to allow asset creation
            using (AssetDatabaseBatchHelper.PauseBatch())
            {
                try
                {
                    AssetDatabase.CreateAsset(created, ScriptableObjectSingletonMetadata.AssetPath);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.ImportAsset(ScriptableObjectSingletonMetadata.AssetPath);
                    return created;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"ScriptableObjectSingletonMetadataUtility: Failed to create metadata asset: {ex.Message}"
                    );
                    if (created != null)
                    {
                        Object.DestroyImmediate(created);
                    }
                    return null;
                }
            }
        }

        private static ScriptableObjectSingletonMetadata MigrateLegacyMetadata(
            ScriptableObjectSingletonMetadata legacyMetadata
        )
        {
            // If we're inside a batch scope, temporarily exit to allow asset operations
            using (AssetDatabaseBatchHelper.PauseBatch())
            {
                if (!EnsureResourcesFolder())
                {
                    Debug.LogWarning(
                        "ScriptableObjectSingletonMetadataUtility: Could not ensure Resources folder exists. Keeping legacy metadata asset."
                    );
                    return legacyMetadata;
                }

                string legacyPath = ScriptableObjectSingletonMetadata.LegacyAssetPath;
                string targetPath = ScriptableObjectSingletonMetadata.AssetPath;

                string moveResult = AssetDatabase.MoveAsset(legacyPath, targetPath);
                if (string.IsNullOrEmpty(moveResult))
                {
                    // Move succeeded - try to delete empty parent folders
                    TryDeleteEmptyParentFolders(legacyPath);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    return AssetDatabase.LoadAssetAtPath<ScriptableObjectSingletonMetadata>(
                        targetPath
                    );
                }

                // Move failed - create new and copy data
                Debug.LogWarning(
                    $"Failed to move ScriptableObjectSingletonMetadata from {legacyPath} to {targetPath}: {moveResult}. Creating new asset."
                );

                ScriptableObjectSingletonMetadata created =
                    ScriptableObject.CreateInstance<ScriptableObjectSingletonMetadata>();
                try
                {
                    AssetDatabase.CreateAsset(created, targetPath);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"ScriptableObjectSingletonMetadataUtility: Failed to create new metadata asset during migration: {ex.Message}. Keeping legacy metadata."
                    );
                    if (created != null)
                    {
                        Object.DestroyImmediate(created);
                    }
                    return legacyMetadata;
                }

                // Delete legacy asset
                if (AssetDatabase.DeleteAsset(legacyPath))
                {
                    TryDeleteEmptyParentFolders(legacyPath);
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                return created;
            }
        }

        private static void TryDeleteEmptyParentFolders(string assetPath)
        {
            try
            {
                string folder = Path.GetDirectoryName(assetPath);
                if (string.IsNullOrWhiteSpace(folder))
                {
                    return;
                }

                folder = folder.SanitizePath();
                while (
                    !string.IsNullOrWhiteSpace(folder)
                    && !string.Equals(folder, "Assets", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(
                        folder,
                        "Assets/Resources",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    if (!AssetDatabase.IsValidFolder(folder))
                    {
                        folder = Path.GetDirectoryName(folder);
                        if (folder != null)
                        {
                            folder = folder.SanitizePath();
                        }
                        continue;
                    }

                    string[] contents = AssetDatabase.FindAssets(string.Empty, new[] { folder });
                    if (contents == null || contents.Length == 0)
                    {
                        if (AssetDatabase.DeleteAsset(folder))
                        {
                            string parent = Path.GetDirectoryName(folder);
                            if (parent != null)
                            {
                                folder = parent.SanitizePath();
                            }
                            else
                            {
                                break;
                            }
                            continue;
                        }
                    }
                    break;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to clean up empty folders after migration: {e.Message}");
            }
        }

        internal static bool UpdateEntry(
            Type type,
            string resourcesLoadPath,
            string resourcesPath,
            string assetGuid
        )
        {
            // Skip creating new metadata asset during test runs to avoid Unity's internal modal dialogs
            // when asset operations fail, unless explicitly allowed. Only update if the asset already exists.
            if (
                EditorUi.Suppress
                && !ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression
            )
            {
                ScriptableObjectSingletonMetadata existing =
                    AssetDatabase.LoadAssetAtPath<ScriptableObjectSingletonMetadata>(
                        ScriptableObjectSingletonMetadata.AssetPath
                    );
                if (existing == null)
                {
                    // Also check legacy path
                    existing = AssetDatabase.LoadAssetAtPath<ScriptableObjectSingletonMetadata>(
                        ScriptableObjectSingletonMetadata.LegacyAssetPath
                    );
                }
                if (existing == null)
                {
                    return false;
                }
            }

            ScriptableObjectSingletonMetadata metadata = LoadOrCreateMetadataAsset();
            if (metadata == null)
            {
                return false;
            }

            ScriptableObjectSingletonMetadata.Entry entry = new()
            {
                assemblyQualifiedTypeName = type.AssemblyQualifiedName,
                resourcesLoadPath = resourcesLoadPath,
                resourcesPath = resourcesPath,
                assetGuid = assetGuid,
            };
            if (!metadata.SetOrUpdateEntry(entry))
            {
                return false;
            }

            EditorUtility.SetDirty(metadata);
            return true;
        }

        /// <summary>
        /// Removes metadata entries that point to non-existent assets.
        /// This cleans up stale entries that may have been left behind when assets were deleted.
        /// </summary>
        /// <returns>The number of stale entries removed.</returns>
        internal static int CleanupStaleEntries()
        {
            ScriptableObjectSingletonMetadata metadata =
                AssetDatabase.LoadAssetAtPath<ScriptableObjectSingletonMetadata>(
                    ScriptableObjectSingletonMetadata.AssetPath
                );
            if (metadata == null)
            {
                // Also check legacy path
                metadata = AssetDatabase.LoadAssetAtPath<ScriptableObjectSingletonMetadata>(
                    ScriptableObjectSingletonMetadata.LegacyAssetPath
                );
            }

            if (metadata == null)
            {
                return 0;
            }

            IReadOnlyList<ScriptableObjectSingletonMetadata.Entry> entries =
                metadata.GetAllEntries();
            if (entries == null || entries.Count == 0)
            {
                return 0;
            }

            using PooledResource<List<string>> staleEntryResource = Buffers<string>.List.Get(
                out List<string> staleEntries
            );
            foreach (ScriptableObjectSingletonMetadata.Entry entry in entries)
            {
                if (string.IsNullOrEmpty(entry.resourcesLoadPath))
                {
                    continue;
                }

                // Check if the asset actually exists
                string assetPath = $"Assets/Resources/{entry.resourcesLoadPath}.asset";
                Object asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                if (asset == null)
                {
                    // Also verify by GUID if available
                    if (!string.IsNullOrEmpty(entry.assetGuid))
                    {
                        string guidPath = AssetDatabase.GUIDToAssetPath(entry.assetGuid);
                        if (!string.IsNullOrEmpty(guidPath))
                        {
                            asset = AssetDatabase.LoadAssetAtPath<Object>(guidPath);
                        }
                    }

                    if (asset == null)
                    {
                        staleEntries.Add(entry.assemblyQualifiedTypeName);
                    }
                }
            }

            if (staleEntries.Count == 0)
            {
                return 0;
            }

            foreach (string typeName in staleEntries)
            {
                metadata.RemoveEntry(typeName);
            }

            EditorUtility.SetDirty(metadata);
            return staleEntries.Count;
        }

        private static bool EnsureResourcesFolder()
        {
            string assetPath = ScriptableObjectSingletonMetadata.AssetPath;
            string directory = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrEmpty(directory))
            {
                return false;
            }

            // Route through the single batch-safe folder helper. It pauses any active batch and
            // creates each missing segment via AssetDatabase.CreateFolder (no raw disk creation,
            // which would leave the AssetDatabase out of sync and spawn numbered duplicates).
            if (!AssetDatabaseBatchHelper.EnsureAssetFolder(directory))
            {
                Debug.LogError(
                    $"ScriptableObjectSingletonMetadataUtility: Failed to ensure folder '{directory.SanitizePath()}'."
                );
                return false;
            }

            return true;
        }

        /// <summary>
        /// Resets legacy state for testing. AssetDatabase batch cleanup is now handled
        /// by the unified <see cref="AssetDatabaseBatchHelper"/>.
        /// </summary>
        /// <remarks>
        /// This method is kept for backward compatibility with test cleanup code.
        /// The actual AssetDatabase state cleanup is handled by AssetDatabaseBatchHelper.ResetBatchDepth().
        /// </remarks>
        internal static void ResetAssetEditingDepthForTesting()
        {
            // AssetDatabase batch cleanup is now handled by AssetDatabaseBatchHelper.ResetBatchDepth()
            // which is called by CommonTestBase in setUp/tearDown.
            // This method is a no-op kept for backward compatibility.
        }

        /// <summary>
        /// Registers the sync implementation with the Runtime metadata class.
        /// Called automatically via InitializeOnLoadMethod.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void RegisterSyncImplementation()
        {
            ScriptableObjectSingletonMetadata.SyncImplementation = SyncAllSingletonMetadata;
        }

        /// <summary>
        /// Re-scans all assemblies for ScriptableObjectSingleton types and updates their metadata entries.
        /// This removes stale entries and adds/updates metadata for all existing singleton assets.
        /// </summary>
        /// <param name="metadata">The metadata asset to sync. If null, loads or creates the metadata asset.</param>
        internal static void SyncAllSingletonMetadata(ScriptableObjectSingletonMetadata metadata)
        {
            metadata ??= LoadOrCreateMetadataAsset();
            if (metadata == null)
            {
                Debug.LogWarning(
                    "ScriptableObjectSingletonMetadataUtility.SyncAllSingletonMetadata: "
                        + "Could not load or create metadata asset."
                );
                return;
            }

            int added = 0;
            int updated = 0;
            int removed = 0;

            // Build a set of existing entries for comparison
            IReadOnlyList<ScriptableObjectSingletonMetadata.Entry> existingEntries =
                metadata.GetAllEntries();
            Dictionary<string, ScriptableObjectSingletonMetadata.Entry> existingByTypeName = new(
                StringComparer.Ordinal
            );
            foreach (ScriptableObjectSingletonMetadata.Entry entry in existingEntries)
            {
                if (!string.IsNullOrEmpty(entry.assemblyQualifiedTypeName))
                {
                    existingByTypeName[entry.assemblyQualifiedTypeName] = entry;
                }
            }

            // Track which types we find during scanning
            HashSet<string> foundTypeNames = new(StringComparer.Ordinal);

            // Scan for all singleton types
            foreach (
                Type derivedType in ReflectionHelpers.GetTypesDerivedFrom(
                    typeof(ScriptableObjectSingleton<>),
                    includeAbstract: false
                )
            )
            {
                if (derivedType.IsGenericType)
                {
                    continue;
                }

                // Skip test types unless explicitly included
                if (TestAssemblyHelper.IsTestType(derivedType))
                {
                    continue;
                }

                string assemblyQualifiedName = derivedType.AssemblyQualifiedName;
                if (string.IsNullOrEmpty(assemblyQualifiedName))
                {
                    continue;
                }

                foundTypeNames.Add(assemblyQualifiedName);

                // Find the asset for this type
                string assetPath = FindSingletonAssetPath(derivedType);
                if (string.IsNullOrEmpty(assetPath))
                {
                    // No asset exists - skip (don't create assets, just sync metadata for existing ones)
                    continue;
                }

                string loadPath = ToResourcesLoadPath(assetPath);
                if (string.IsNullOrEmpty(loadPath))
                {
                    continue;
                }

                string resourcesFolder = GetResourcesFolderFromLoadPath(loadPath);
                string guid = AssetDatabase.AssetPathToGUID(assetPath) ?? string.Empty;

                ScriptableObjectSingletonMetadata.Entry newEntry = new()
                {
                    assemblyQualifiedTypeName = assemblyQualifiedName,
                    resourcesLoadPath = loadPath,
                    resourcesPath = resourcesFolder,
                    assetGuid = guid,
                };

                // Check if entry exists and needs updating
                if (
                    existingByTypeName.TryGetValue(
                        assemblyQualifiedName,
                        out ScriptableObjectSingletonMetadata.Entry existingEntry
                    )
                )
                {
                    bool needsUpdate =
                        !string.Equals(
                            existingEntry.resourcesLoadPath,
                            newEntry.resourcesLoadPath,
                            StringComparison.Ordinal
                        )
                        || !string.Equals(
                            existingEntry.resourcesPath,
                            newEntry.resourcesPath,
                            StringComparison.Ordinal
                        )
                        || !string.Equals(
                            existingEntry.assetGuid,
                            newEntry.assetGuid,
                            StringComparison.Ordinal
                        );

                    if (needsUpdate)
                    {
                        metadata.SetOrUpdateEntry(newEntry);
                        updated++;
                    }
                }
                else
                {
                    metadata.SetOrUpdateEntry(newEntry);
                    added++;
                }
            }

            // Remove stale entries (types that no longer exist or have no assets)
            foreach (string existingTypeName in existingByTypeName.Keys)
            {
                if (!foundTypeNames.Contains(existingTypeName))
                {
                    // Type was not found during scan - could be deleted or renamed
                    // Also check if the asset still exists
                    ScriptableObjectSingletonMetadata.Entry staleEntry = existingByTypeName[
                        existingTypeName
                    ];
                    if (!string.IsNullOrEmpty(staleEntry.resourcesLoadPath))
                    {
                        string assetPath = $"Assets/Resources/{staleEntry.resourcesLoadPath}.asset";
                        Object asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                        if (asset == null && !string.IsNullOrEmpty(staleEntry.assetGuid))
                        {
                            string guidPath = AssetDatabase.GUIDToAssetPath(staleEntry.assetGuid);
                            if (!string.IsNullOrEmpty(guidPath))
                            {
                                asset = AssetDatabase.LoadAssetAtPath<Object>(guidPath);
                            }
                        }

                        if (asset == null)
                        {
                            metadata.RemoveEntry(existingTypeName);
                            removed++;
                        }
                    }
                    else
                    {
                        metadata.RemoveEntry(existingTypeName);
                        removed++;
                    }
                }
            }

            if (added > 0 || updated > 0 || removed > 0)
            {
                EditorUtility.SetDirty(metadata);
                AssetDatabase.SaveAssets();
                Debug.Log(
                    $"ScriptableObjectSingletonMetadata.Sync: Added {added}, updated {updated}, removed {removed} entries."
                );
            }
            else
            {
                Debug.Log(
                    "ScriptableObjectSingletonMetadata.Sync: Metadata is already up to date."
                );
            }
        }

        private static string FindSingletonAssetPath(Type type)
        {
            // First try to find by type name in Resources
            string[] guids = AssetDatabase.FindAssets(
                $"t:{type.Name}",
                new[] { "Assets/Resources" }
            );

            if (guids != null && guids.Length > 0)
            {
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path))
                    {
                        continue;
                    }

                    Object asset = AssetDatabase.LoadAssetAtPath(path, type);
                    if (asset != null)
                    {
                        return path;
                    }
                }
            }

            // Try loading from Resources as a fallback
            Object[] instances = Resources.LoadAll(string.Empty, type);
            if (instances != null && instances.Length > 0)
            {
                foreach (Object instance in instances)
                {
                    if (instance == null)
                    {
                        continue;
                    }

                    string path = AssetDatabase.GetAssetPath(instance);
                    if (!string.IsNullOrEmpty(path))
                    {
                        return path;
                    }
                }
            }

            return null;
        }

        private static string ToResourcesLoadPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            const string resourcesRoot = "Assets/Resources";
            string normalized = assetPath.SanitizePath();
            if (!normalized.StartsWith(resourcesRoot, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string relative = normalized.Substring(resourcesRoot.Length).TrimStart('/');
            if (string.IsNullOrWhiteSpace(relative))
            {
                return null;
            }

            if (relative.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                relative = relative.Substring(0, relative.Length - ".asset".Length);
            }

            return relative.Replace("\\", "/");
        }

        private static string GetResourcesFolderFromLoadPath(string loadPath)
        {
            if (string.IsNullOrWhiteSpace(loadPath))
            {
                return string.Empty;
            }

            int lastSlash = loadPath.LastIndexOf('/');
            if (lastSlash <= 0)
            {
                return string.Empty;
            }

            return loadPath.Substring(0, lastSlash);
        }
    }
#endif
}
