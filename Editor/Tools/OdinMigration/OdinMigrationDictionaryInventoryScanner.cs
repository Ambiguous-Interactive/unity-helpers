// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Serialization.OdinMigration;
    using WallstopStudios.UnityHelpers.Editor.Validation;

    internal static class OdinMigrationDictionaryInventoryScanner
    {
        internal static bool TryScan(
            OdinMigrationDictionaryMigrationPreview preview,
            IReadOnlyList<string> assetPaths,
            out OdinMigrationDictionaryInventory inventory,
            out string failure,
            Func<string, int, int, bool> shouldCancel
        )
        {
            List<OdinMigrationAffectedDocument> documents =
                new List<OdinMigrationAffectedDocument>();
            List<string> failures = new List<string>();
            bool cancelled = false;
            int scanned = 0;
            int missing = 0;
            try
            {
                if (
                    preview == null
                    || assetPaths == null
                    || preview.Bindings.Count == 0
                    || preview.Targets.Count == 0
                )
                {
                    throw new InvalidDataException(
                        "A paired preview and complete asset path inventory are required."
                    );
                }
                HashSet<Type> affectedTypes = new HashSet<Type>();
                foreach (
                    OdinMigrationCompiledBinding binding in preview.Bindings.AsValueEnumerableRequired()
                )
                {
                    if (binding == null || binding.AffectedTypes.Count == 0)
                    {
                        throw new InvalidDataException(
                            "An original compiler binding has no affected declaring-type metadata."
                        );
                    }
                    foreach (Type type in binding.AffectedTypes.AsValueEnumerableRequired())
                    {
                        if (type == null)
                        {
                            throw new InvalidDataException(
                                "An affected declaring type is unavailable."
                            );
                        }
                        affectedTypes.Add(type);
                    }
                    foreach (
                        OdinMigrationCompiledSource source in binding.Sources.AsValueEnumerableRequired()
                    )
                    {
                        OdinMigrationSourceBinder.ReadCompiledSource(source);
                    }
                }
                Dictionary<string, HashSet<long>> selected = new Dictionary<string, HashSet<long>>(
                    StringComparer.Ordinal
                );
                foreach (
                    OdinMigrationDictionaryTarget target in preview.Targets.AsValueEnumerableRequired()
                )
                {
                    if (!selected.TryGetValue(target.AssetPath, out HashSet<long> ids))
                    {
                        ids = new HashSet<long>();
                        selected.Add(target.AssetPath, ids);
                    }
                    ids.Add(target.FileId);
                }
                Dictionary<string, Type> scriptTypes = new Dictionary<string, Type>(
                    StringComparer.OrdinalIgnoreCase
                );
                HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal);
                HashSet<string> capturedSelections = new HashSet<string>(StringComparer.Ordinal);
                int index = 0;
                foreach (string path in assetPaths.AsValueEnumerableRequired())
                {
                    if (shouldCancel != null && shouldCancel(path, index, assetPaths.Count))
                    {
                        cancelled = true;
                        break;
                    }
                    ++index;
                    if (!visited.Add(path))
                    {
                        continue;
                    }
                    try
                    {
                        OdinMigrationTool.GetValidatedAssetPath(path);
                        if (
                            !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                            && !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                            && !path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)
                        )
                        {
                            throw new InvalidDataException(
                                "The affected-asset inventory contains a non-Unity asset path."
                            );
                        }
                        if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                        {
                            Type mainType = AssetDatabase.GetMainAssetTypeAtPath(path);
                            if (
                                mainType != null
                                && mainType != typeof(DefaultAsset)
                                && !typeof(UnityEngine.ScriptableObject).IsAssignableFrom(mainType)
                                && !typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(mainType)
                            )
                            {
                                if (selected.ContainsKey(path))
                                {
                                    throw new InvalidDataException(
                                        "A selected asset's original Unity type is inconsistent."
                                    );
                                }
                                ++scanned;
                                continue;
                            }
                        }
                        string source = OdinMigrationDictionaryMigrationBuilder.ReadAsset(path);
                        ++scanned;
                        if (
                            preview.Assets.TryGetValue(
                                path,
                                out OdinMigrationAssetProposal proposal
                            )
                            && !string.Equals(
                                proposal.OriginalSource,
                                source,
                                StringComparison.Ordinal
                            )
                        )
                        {
                            throw new InvalidDataException(
                                "A selected asset changed after its paired preview."
                            );
                        }
                        IReadOnlyList<AuthoredAssetDocument> authored =
                            OdinMigrationDictionaryMigrationBuilder.ReadDocuments(source);
                        HashSet<long> fileIds = new HashSet<long>();
                        foreach (
                            AuthoredAssetDocument document in authored.AsValueEnumerableRequired()
                        )
                        {
                            if (!fileIds.Add(document.FileId))
                            {
                                throw new InvalidDataException(
                                    "A saved Unity document identifier occurs more than once."
                                );
                            }
                            if (document.UnityTypeId != 114)
                            {
                                continue;
                            }
                            if (
                                !document.TryGetEntry(
                                    AuthoredAssetDocument.ScriptKey,
                                    out AuthoredAssetEntry script
                                )
                            )
                            {
                                throw new InvalidDataException(
                                    "A saved Unity document has no original script reference."
                                );
                            }
                            if (
                                !AuthoredAssetYaml.TryParseObjectReference(
                                    script.InlineValue,
                                    out long fileId,
                                    out string scriptGuid
                                )
                                || fileId != 11500000
                                || string.IsNullOrWhiteSpace(scriptGuid)
                            )
                            {
                                throw new InvalidDataException(
                                    "A Unity script reference cannot be resolved from its authored metadata."
                                );
                            }
                            if (!scriptTypes.TryGetValue(scriptGuid, out Type originalType))
                            {
                                string scriptPath = AssetDatabase.GUIDToAssetPath(scriptGuid);
                                if (string.IsNullOrWhiteSpace(scriptPath))
                                {
                                    throw new InvalidDataException(
                                        "A saved Unity document's original script path is unavailable."
                                    );
                                }
                                MonoScript monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(
                                    scriptPath
                                );
                                originalType = monoScript == null ? null : monoScript.GetClass();
                                if (originalType == null)
                                {
                                    throw new InvalidDataException(
                                        "A saved Unity document's original script type is unavailable."
                                    );
                                }
                                scriptTypes.Add(scriptGuid, originalType);
                            }
                            bool affected = false;
                            foreach (Type declaringType in affectedTypes)
                            {
                                if (declaringType.IsAssignableFrom(originalType))
                                {
                                    affected = true;
                                    break;
                                }
                            }
                            if (!affected)
                            {
                                continue;
                            }
                            bool retained =
                                document.TryGetEntry("serializationData", out AuthoredAssetEntry _)
                                || document.TryGetEntry("_serializationData", out _);
                            bool included =
                                selected.TryGetValue(path, out HashSet<long> ids)
                                && ids.Contains(document.FileId);
                            if (retained && !included)
                            {
                                ++missing;
                            }
                            documents.Add(
                                new OdinMigrationAffectedDocument(
                                    path,
                                    document.FileId,
                                    scriptGuid,
                                    originalType,
                                    retained,
                                    included
                                )
                            );
                        }
                        if (selected.ContainsKey(path))
                        {
                            capturedSelections.Add(path);
                        }
                    }
                    catch (Exception exception)
                    {
                        failures.Add(path + ": " + exception.Message);
                    }
                }
                foreach (string path in selected.Keys)
                {
                    if (!cancelled && !capturedSelections.Contains(path))
                    {
                        failures.Add(
                            path + ": A selected asset is missing from the complete inventory."
                        );
                    }
                }
                bool complete = !cancelled && failures.Count == 0;
                inventory = new OdinMigrationDictionaryInventory(
                    complete,
                    cancelled,
                    scanned,
                    missing,
                    documents,
                    failures
                );
                failure =
                    complete ? null
                    : cancelled ? "The affected-asset inventory was cancelled."
                    : string.Join("; ", failures);
                return complete;
            }
            catch (Exception exception)
            {
                failures.Add(exception.Message);
                inventory = new OdinMigrationDictionaryInventory(
                    false,
                    cancelled,
                    scanned,
                    missing,
                    documents,
                    failures
                );
                failure = exception.Message;
                return false;
            }
        }
    }
}
