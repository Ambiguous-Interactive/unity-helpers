// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>Copies effective pivots between imported Single-mode sprites.</summary>
    public static class SpritePivotCopierAPI
    {
        /// <summary>The default source-name marker.</summary>
        public const string DefaultMarker = "_pivot_";

        /// <summary>The default replacement used to find counterpart sprites.</summary>
        public const string DefaultReplacement = "_";

        /// <summary>Copies a source sprite's normalized pivot and uses Custom alignment on the target.</summary>
        /// <remarks>Records importer Undo. Reimport and file effects are not fully reversible; an import failure can leave settings changed.</remarks>
        public static bool TryCopyPivot(
            Sprite source,
            Sprite target,
            out bool changed,
            out string error
        )
        {
            try
            {
                if (
                    !TryReadCopy(
                        source,
                        target,
                        out TextureImporter importer,
                        out Vector2 pivot,
                        out error
                    )
                )
                {
                    changed = false;
                    return false;
                }
                return TryApplyPivot(importer, pivot, out changed, out error);
            }
            catch (Exception exception)
            {
                error =
                    "Pivot copy failed; inspect the target importer before retrying. "
                    + exception.Message;
                changed = false;
                return false;
            }
        }

        /// <summary>Copies pivots to uniquely named counterparts beneath an Assets folder.</summary>
        /// <remarks>
        /// Names, importers and source pivots are captured before writing. Invalid plans change nothing.
        /// Completed copies remain on cancellation or later import failure; changedCount reports completed changes.
        /// The optional cancellation callback receives processed entries, total entries and the next target path.
        /// Source markers use ordinal replacement; a null replacement removes the marker.
        /// </remarks>
        public static bool TryCopyPivotsInDirectory(
            string directory,
            out int changedCount,
            out string error,
            string marker = DefaultMarker,
            string replacement = DefaultReplacement,
            Func<int, int, string, bool> cancelRequested = null
        )
        {
            int completed = 0;
            if (
                string.IsNullOrWhiteSpace(directory)
                || string.IsNullOrWhiteSpace(marker)
                || string.Equals(marker, replacement, StringComparison.Ordinal)
            )
            {
                error =
                    "An Assets folder and a nonblank source marker different from its replacement are required.";
                changedCount = completed;
                return false;
            }
            string normalized = directory.Replace('\\', '/').TrimEnd('/');
            if (
                !(
                    string.Equals(normalized, "Assets", StringComparison.Ordinal)
                    || normalized.StartsWith("Assets/", StringComparison.Ordinal)
                )
                || normalized.Contains("/../", StringComparison.Ordinal)
                || normalized.EndsWith("/..", StringComparison.Ordinal)
            )
            {
                error = "Choose a project folder beneath Assets.";
                changedCount = completed;
                return false;
            }
            try
            {
                if (!AssetDatabase.IsValidFolder(normalized))
                {
                    error = "The sprite folder does not exist: " + normalized;
                    changedCount = completed;
                    return false;
                }
                using PooledResource<Dictionary<string, Sprite>> namesLease = DictionaryBuffer<
                    string,
                    Sprite
                >
                    .GetDictionaryPool(StringComparer.Ordinal)
                    .Get(out Dictionary<string, Sprite> names);
                using PooledResource<Dictionary<TextureImporter, Vector2>> planLease =
                    DictionaryBuffer<TextureImporter, Vector2>.Dictionary.Get(
                        out Dictionary<TextureImporter, Vector2> plan
                    );
                string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { normalized });
                foreach (string guid in guids)
                {
                    Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                        AssetDatabase.GUIDToAssetPath(guid)
                    );
                    if (sprite == null)
                    {
                        continue;
                    }
                    string name = sprite.name;
                    if (!names.TryAdd(name, sprite))
                    {
                        names[name] = null;
                    }
                }
                foreach (KeyValuePair<string, Sprite> entry in names)
                {
                    if (!entry.Key.Contains(marker, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    string targetName = entry.Key.Replace(
                        marker,
                        replacement ?? string.Empty,
                        StringComparison.Ordinal
                    );
                    if (
                        entry.Value == null
                        || !names.TryGetValue(targetName, out Sprite target)
                        || target == null
                    )
                    {
                        error =
                            "The source or its counterpart is missing or ambiguous: "
                            + entry.Key
                            + " -> "
                            + targetName;
                        changedCount = completed;
                        return false;
                    }
                    if (
                        !TryReadCopy(
                            entry.Value,
                            target,
                            out TextureImporter importer,
                            out Vector2 pivot,
                            out error
                        )
                    )
                    {
                        changedCount = completed;
                        return false;
                    }
                    if (!plan.TryAdd(importer, pivot))
                    {
                        error = "Multiple pivot sources select the same target: " + targetName;
                        changedCount = completed;
                        return false;
                    }
                }
                if (plan.Count == 0)
                {
                    error = "No sprite names contain the configured source marker.";
                    changedCount = completed;
                    return false;
                }
                int processed = 0;
                foreach (KeyValuePair<TextureImporter, Vector2> operation in plan)
                {
                    if (
                        cancelRequested != null
                        && cancelRequested(
                            processed,
                            plan.Count,
                            operation.Key != null ? operation.Key.assetPath : string.Empty
                        )
                    )
                    {
                        error = "Pivot copy cancelled after " + completed + " completed changes.";
                        changedCount = completed;
                        return false;
                    }
                    if (!TryApplyPivot(operation.Key, operation.Value, out bool changed, out error))
                    {
                        changedCount = completed;
                        return false;
                    }
                    if (changed)
                    {
                        ++completed;
                    }
                    ++processed;
                }
                error = null;
                changedCount = completed;
                return true;
            }
            catch (Exception exception)
            {
                error =
                    "Pivot copy failed after "
                    + completed
                    + " completed changes. "
                    + exception.Message;
                changedCount = completed;
                return false;
            }
        }

        internal static bool TryNormalizePivot(Vector2 pixels, Vector2 size, out Vector2 normalized)
        {
            if (
                !float.IsFinite(pixels.x)
                || !float.IsFinite(pixels.y)
                || !float.IsFinite(size.x)
                || !float.IsFinite(size.y)
                || !(0f < size.x)
                || !(0f < size.y)
            )
            {
                normalized = default;
                return false;
            }
            Vector2 result = new(pixels.x / size.x, pixels.y / size.y);
            if (!float.IsFinite(result.x) || !float.IsFinite(result.y))
            {
                normalized = default;
                return false;
            }
            normalized = result;
            return true;
        }

        private static bool TryReadCopy(
            Sprite source,
            Sprite target,
            out TextureImporter targetImporter,
            out Vector2 pivot,
            out string error
        )
        {
            if (source == null || target == null)
            {
                error = "Live source and target sprites are required.";
                targetImporter = null;
                pivot = default;
                return false;
            }
            string sourcePath = AssetDatabase.GetAssetPath(source);
            string targetPath = AssetDatabase.GetAssetPath(target);
            if (!targetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                error = "The target sprite must be an editable project asset under Assets.";
                targetImporter = null;
                pivot = default;
                return false;
            }
            TextureImporter sourceImporter = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
            TextureImporter targetMetadata = AssetImporter.GetAtPath(targetPath) as TextureImporter;
            if (
                sourceImporter == null
                || targetMetadata == null
                || sourceImporter.textureType != TextureImporterType.Sprite
                || targetMetadata.textureType != TextureImporterType.Sprite
                || sourceImporter.spriteImportMode != SpriteImportMode.Single
                || targetMetadata.spriteImportMode != SpriteImportMode.Single
            )
            {
                error = "Both sprites must belong to Single-mode texture importers.";
                targetImporter = null;
                pivot = default;
                return false;
            }
            if (!TryNormalizePivot(source.pivot, source.rect.size, out Vector2 normalizedPivot))
            {
                error = "The source sprite has an unrepresentable normalized pivot.";
                targetImporter = null;
                pivot = default;
                return false;
            }
            if (
                !AssetDatabase.IsOpenForEdit(targetPath, StatusQueryOptions.ForceUpdate)
                || !IsMetadataEditable(targetMetadata)
            )
            {
                error = "The target texture or metadata is not open for editing: " + targetPath;
                targetImporter = null;
                pivot = default;
                return false;
            }
            targetImporter = targetMetadata;
            pivot = normalizedPivot;
            error = null;
            return true;
        }

        private static bool IsMetadataEditable(TextureImporter importer)
        {
            if (!AssetDatabase.IsMetaFileOpenForEdit(importer, StatusQueryOptions.ForceUpdate))
            {
                return false;
            }
            string projectDirectory = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrWhiteSpace(projectDirectory))
            {
                return false;
            }
            string metadataPath = Path.Combine(projectDirectory, importer.assetPath + ".meta");
            return (File.GetAttributes(metadataPath) & FileAttributes.ReadOnly) == 0;
        }

        private static bool TryApplyPivot(
            TextureImporter importer,
            Vector2 pivot,
            out bool changed,
            out string error
        )
        {
            if (importer == null)
            {
                error = "The target texture importer no longer exists.";
                changed = false;
                return false;
            }
            if (
                !importer.assetPath.StartsWith("Assets/", StringComparison.Ordinal)
                || !AssetDatabase.IsOpenForEdit(importer.assetPath, StatusQueryOptions.ForceUpdate)
                || !IsMetadataEditable(importer)
            )
            {
                error = "The target texture or metadata is no longer an editable project asset.";
                changed = false;
                return false;
            }
            TextureImporterSettings settings = new();
            importer.ReadTextureSettings(settings);
            if (
                settings.spriteAlignment == (int)SpriteAlignment.Custom
                && settings.spritePivot.Equals(pivot)
            )
            {
                changed = false;
                error = null;
                return true;
            }
            using (AssetDatabaseBatchHelper.PauseBatch())
            {
                Undo.RecordObject(importer, "Copy Sprite Pivot");
                settings.spriteAlignment = (int)SpriteAlignment.Custom;
                settings.spritePivot = pivot;
                importer.SetTextureSettings(settings);
                EditorUtility.SetDirty(importer);
                Undo.FlushUndoRecordObjects();
                importer.SaveAndReimport();
            }
            changed = true;
            error = null;
            return true;
        }
    }
#endif
}
