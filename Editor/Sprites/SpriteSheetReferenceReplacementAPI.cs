// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Reports sprite reference matches and changed assets.
    /// </summary>
    public sealed class SpriteReferenceReplacementResult
    {
        /// <summary>Gets the number of assets changed or that would change.</summary>
        public int ModifiedAssets { get; internal set; }

        /// <summary>Gets the number of sprite references found.</summary>
        public int MatchedReferences { get; internal set; }

        /// <summary>Gets whether a progress callback canceled the scan.</summary>
        public bool Canceled { get; internal set; }

        /// <summary>Gets errors encountered while scanning or saving.</summary>
        public IReadOnlyList<string> Errors => _errors;

        private readonly List<string> _errors = new();

        internal void AddError(string error)
        {
            _errors.Add(error);
        }
    }

    /// <summary>
    /// Previews or replaces sprite references in explicit project assets.
    /// </summary>
    public static class SpriteSheetReferenceReplacementAPI
    {
        private static readonly string[] CandidateExtensions =
        {
            ".prefab",
            ".unity",
            ".asset",
            ".mat",
            ".anim",
            ".overrideController",
        };

        /// <summary>
        /// Scans explicit asset paths and optionally replaces mapped sprite references.
        /// </summary>
        public static SpriteReferenceReplacementResult Run(
            IReadOnlyDictionary<Sprite, Sprite> replacements,
            IReadOnlyList<string> assetPaths,
            bool applyChanges = false,
            Func<int, int, bool> cancelRequested = null
        )
        {
            SpriteReferenceReplacementResult result = new();
            if (replacements == null || assetPaths == null)
            {
                result.AddError("Sprite mappings and asset paths are required.");
                return result;
            }

            foreach (KeyValuePair<Sprite, Sprite> pair in replacements)
            {
                if (
                    pair.Key == null
                    || pair.Value == null
                    || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(pair.Key))
                    || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(pair.Value))
                )
                {
                    result.AddError(
                        "Sprite mappings must contain persistent source and replacement assets."
                    );
                    return result;
                }
            }

            if (replacements.Count == 0 || assetPaths.Count == 0)
            {
                return result;
            }

            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            AssetDatabaseBatchScope batch = default;
            bool batchStarted = false;
            try
            {
                if (applyChanges)
                {
                    batch = AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false);
                    batchStarted = true;
                }

                for (int i = 0; i < assetPaths.Count; ++i)
                {
                    if (cancelRequested != null)
                    {
                        try
                        {
                            if (cancelRequested(i, assetPaths.Count))
                            {
                                result.Canceled = true;
                                break;
                            }
                        }
                        catch (Exception error)
                        {
                            result.AddError($"Progress callback failed: {error.Message}");
                            break;
                        }
                    }

                    string path = assetPaths[i];
                    if (!IsCandidate(path) || !seen.Add(path))
                    {
                        continue;
                    }

                    try
                    {
                        if (ProcessAsset(path, replacements, applyChanges, result))
                        {
                            ++result.ModifiedAssets;
                        }
                    }
                    catch (Exception error)
                    {
                        result.AddError($"Failed to scan '{path}': {error.Message}");
                    }
                }
            }
            catch (Exception error)
            {
                result.AddError($"Failed to start or finish asset batch: {error.Message}");
            }
            finally
            {
                if (batchStarted)
                {
                    batch.Dispose();
                }
            }

            if (applyChanges && 0 < result.ModifiedAssets)
            {
                try
                {
                    AssetDatabase.SaveAssets();
                }
                catch (Exception error)
                {
                    result.AddError($"Failed to save changed assets: {error.Message}");
                }
            }

            return result;
        }

        private static bool IsCandidate(string path)
        {
            if (
                string.IsNullOrWhiteSpace(path)
                || !path.StartsWith("Assets/", StringComparison.Ordinal)
            )
            {
                return false;
            }

            foreach (string extension in CandidateExtensions)
            {
                if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ProcessAsset(
            string path,
            IReadOnlyDictionary<Sprite, Sprite> replacements,
            bool applyChanges,
            SpriteReferenceReplacementResult result
        )
        {
            bool assetModified = false;
            Object[] objects = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (Object target in objects)
            {
                if (target == null)
                {
                    continue;
                }

                try
                {
                    using SerializedObject serialized = new(target);
                    serialized.Update();
                    using SerializedProperty property = serialized.GetIterator();
                    bool enterChildren = true;
                    bool objectChanged = false;
                    while (property.NextVisible(enterChildren))
                    {
                        enterChildren = false;
                        if (property.propertyType != SerializedPropertyType.ObjectReference)
                        {
                            continue;
                        }

                        Sprite source = property.objectReferenceValue as Sprite;
                        if (
                            source == null
                            || !replacements.TryGetValue(source, out Sprite replacement)
                            || replacement == source
                        )
                        {
                            continue;
                        }

                        ++result.MatchedReferences;
                        if (!applyChanges)
                        {
                            assetModified = true;
                            continue;
                        }

                        if (!objectChanged)
                        {
                            Undo.RecordObject(target, "Replace sprite references");
                            objectChanged = true;
                        }

                        property.objectReferenceValue = replacement;
                    }

                    if (objectChanged)
                    {
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                        Undo.FlushUndoRecordObjects();
                        assetModified = true;
                        EditorUtility.SetDirty(target);
                    }
                }
                catch (Exception error)
                {
                    result.AddError(
                        $"Failed to inspect '{path}' object '{target.name}': {error.Message}"
                    );
                }
            }

            return assetModified;
        }
    }
#endif
}
