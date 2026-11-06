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

        private const string AssetsRoot = "Assets";
        private const string AssetsPrefix = AssetsRoot + "/";
        private const string ParentSegment = "/../";
        private const string TrailingParentSegment = "/..";
        private const string SpriteSearchFilter = "t:Sprite";
        private const string MetadataSuffix = ".meta";
        private const string UndoName = "Copy Sprite Pivot";
        private const char DirectorySeparator = '/';

        private static readonly Func<TextureImporterSettings> SettingsFactory =
            CreateTextureSettings;
        private static readonly WallstopGenericPool<TextureImporterSettings> SettingsPool = new(
            SettingsFactory
        );

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
            string normalized = PathHelper.Sanitize(directory);
            normalized = normalized.TrimEnd(DirectorySeparator);
            if (
                !(
                    string.Equals(normalized, AssetsRoot, StringComparison.Ordinal)
                    || normalized.StartsWith(AssetsPrefix, StringComparison.Ordinal)
                )
                || normalized.Contains(ParentSegment, StringComparison.Ordinal)
                || normalized.EndsWith(TrailingParentSegment, StringComparison.Ordinal)
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
                StringComparer comparer = StringComparer.Ordinal;
                WallstopGenericPool<Dictionary<string, Sprite>> namesPool = DictionaryBuffer<
                    string,
                    Sprite
                >.GetDictionaryPool(comparer);
                using PooledResource<Dictionary<string, Sprite>> namesLease = namesPool.Get(
                    out Dictionary<string, Sprite> names
                );
                WallstopGenericPool<HashSet<string>> ambiguousPool =
                    SetBuffers<string>.GetHashSetPool(comparer);
                using PooledResource<HashSet<string>> ambiguousLease = ambiguousPool.Get(
                    out HashSet<string> ambiguousNames
                );
                WallstopGenericPool<Dictionary<TextureImporter, Vector2>> planPool =
                    DictionaryBuffer<TextureImporter, Vector2>.Dictionary;
                using PooledResource<Dictionary<TextureImporter, Vector2>> planLease = planPool.Get(
                    out Dictionary<TextureImporter, Vector2> plan
                );
                string[] searchFolders = { normalized };
                string[] guids = AssetDatabase.FindAssets(SpriteSearchFilter, searchFolders);
                string normalizedReplacement = replacement ?? string.Empty;
                foreach (string guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                    if (sprite == null)
                    {
                        continue;
                    }
                    string name = sprite.name;
                    if (!names.TryAdd(name, sprite))
                    {
                        ambiguousNames.Add(name);
                    }
                }
                foreach (KeyValuePair<string, Sprite> entry in names)
                {
                    string sourceName = entry.Key;
                    Sprite sourceSprite = entry.Value;
                    if (!sourceName.Contains(marker, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    string targetName = sourceName.Replace(
                        marker,
                        normalizedReplacement,
                        StringComparison.Ordinal
                    );
                    if (
                        ambiguousNames.Contains(sourceName)
                        || ambiguousNames.Contains(targetName)
                        || sourceSprite == null
                        || !names.TryGetValue(targetName, out Sprite target)
                        || target == null
                    )
                    {
                        error =
                            "The source or its counterpart is missing or ambiguous: "
                            + sourceName
                            + " -> "
                            + targetName;
                        changedCount = completed;
                        return false;
                    }
                    if (
                        !TryReadCopy(
                            sourceSprite,
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
                    if (!IsEditableTarget(importer))
                    {
                        string targetPath = importer.assetPath;
                        error =
                            "The target texture or metadata is not open for editing: " + targetPath;
                        changedCount = completed;
                        return false;
                    }
                    using PooledResource<TextureImporterSettings> targetSettingsLease =
                        SettingsPool.Get(out TextureImporterSettings targetSettings);
                    importer.ReadTextureSettings(targetSettings);
                    int targetAlignment = targetSettings.spriteAlignment;
                    if (!IsSupportedAlignment(targetAlignment))
                    {
                        error = "The target sprite alignment is unsupported.";
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
                int total = plan.Count;
                foreach (KeyValuePair<TextureImporter, Vector2> operation in plan)
                {
                    TextureImporter importer = operation.Key;
                    string nextPath = importer != null ? importer.assetPath : string.Empty;
                    if (cancelRequested != null && cancelRequested(processed, total, nextPath))
                    {
                        error = "Pivot copy cancelled after " + completed + " completed changes.";
                        changedCount = completed;
                        return false;
                    }
                    Vector2 plannedPivot = operation.Value;
                    if (!TryApplyPivot(importer, plannedPivot, out bool changed, out error))
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
                !WallMath.IsFinite(pixels)
                || !WallMath.IsFinite(size)
                || !(0f < size.x)
                || !(0f < size.y)
            )
            {
                normalized = default;
                return false;
            }
            float normalizedX = pixels.x / size.x;
            float normalizedY = pixels.y / size.y;
            Vector2 result = new(normalizedX, normalizedY);
            if (!WallMath.IsFinite(result))
            {
                normalized = default;
                return false;
            }
            normalized = result;
            return true;
        }

        internal static bool IsSupportedAlignment(int alignment)
        {
            SpriteAlignment spriteAlignment = (SpriteAlignment)alignment;
            switch (spriteAlignment)
            {
                case SpriteAlignment.Center:
                case SpriteAlignment.TopLeft:
                case SpriteAlignment.TopCenter:
                case SpriteAlignment.TopRight:
                case SpriteAlignment.LeftCenter:
                case SpriteAlignment.RightCenter:
                case SpriteAlignment.BottomLeft:
                case SpriteAlignment.BottomCenter:
                case SpriteAlignment.BottomRight:
                case SpriteAlignment.Custom:
                    return true;
                default:
                    return false;
            }
        }

        private static TextureImporterSettings CreateTextureSettings()
        {
            return new TextureImporterSettings();
        }

        private static bool IsSingleSpriteImporter(TextureImporter importer)
        {
            return importer != null
                && importer.textureType == TextureImporterType.Sprite
                && importer.spriteImportMode == SpriteImportMode.Single;
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
            string targetPath = AssetDatabase.GetAssetPath(target);
            if (!targetPath.StartsWith(AssetsPrefix, StringComparison.Ordinal))
            {
                error = "The target sprite must be an editable project asset under Assets.";
                targetImporter = null;
                pivot = default;
                return false;
            }
            TextureImporter targetMetadata = AssetImporter.GetAtPath(targetPath) as TextureImporter;
            if (!IsSingleSpriteImporter(targetMetadata))
            {
                error = "The target must belong to a Single-mode sprite importer.";
                targetImporter = null;
                pivot = default;
                return false;
            }
            string sourcePath = AssetDatabase.GetAssetPath(source);
            TextureImporter sourceImporter = AssetImporter.GetAtPath(sourcePath) as TextureImporter;
            if (!IsSingleSpriteImporter(sourceImporter))
            {
                error = "The source must belong to a Single-mode sprite importer.";
                targetImporter = null;
                pivot = default;
                return false;
            }
            Vector2 sourcePivot = source.pivot;
            Rect sourceRect = source.rect;
            Vector2 sourceSize = sourceRect.size;
            if (!TryNormalizePivot(sourcePivot, sourceSize, out Vector2 normalizedPivot))
            {
                error = "The source sprite has an unrepresentable normalized pivot.";
                targetImporter = null;
                pivot = default;
                return false;
            }
            targetImporter = targetMetadata;
            pivot = normalizedPivot;
            error = null;
            return true;
        }

        private static bool IsEditableTarget(TextureImporter importer)
        {
            string targetPath = importer.assetPath;
            return targetPath.StartsWith(AssetsPrefix, StringComparison.Ordinal)
                && AssetDatabase.IsOpenForEdit(targetPath, StatusQueryOptions.ForceUpdate)
                && IsMetadataEditable(importer);
        }

        private static bool IsMetadataEditable(TextureImporter importer)
        {
            if (!AssetDatabase.IsMetaFileOpenForEdit(importer, StatusQueryOptions.ForceUpdate))
            {
                return false;
            }
            string assetsDirectory = Application.dataPath;
            string projectDirectory = Path.GetDirectoryName(assetsDirectory);
            if (string.IsNullOrWhiteSpace(projectDirectory))
            {
                return false;
            }
            string relativeMetadataPath = importer.assetPath + MetadataSuffix;
            string metadataPath = Path.Combine(projectDirectory, relativeMetadataPath);
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
            if (!IsSingleSpriteImporter(importer))
            {
                error = "The target texture importer is no longer a Single-mode sprite.";
                changed = false;
                return false;
            }
            if (!IsEditableTarget(importer))
            {
                error = "The target texture or metadata is no longer an editable project asset.";
                changed = false;
                return false;
            }
            using PooledResource<TextureImporterSettings> settingsLease = SettingsPool.Get(
                out TextureImporterSettings settings
            );
            importer.ReadTextureSettings(settings);
            int targetAlignment = settings.spriteAlignment;
            if (!IsSupportedAlignment(targetAlignment))
            {
                error = "The target sprite alignment is unsupported.";
                changed = false;
                return false;
            }
            if (
                targetAlignment == (int)SpriteAlignment.Custom
                && settings.spritePivot.Equals(pivot)
            )
            {
                changed = false;
                error = null;
                return true;
            }
            using (AssetDatabaseBatchHelper.PauseBatch())
            {
                Undo.RecordObject(importer, UndoName);
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
