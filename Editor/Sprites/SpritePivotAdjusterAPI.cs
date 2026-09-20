// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.RegularExpressions;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Utils;

    /// <summary>
    /// Finds sprites and adjusts their pivots without opening an editor window.
    /// </summary>
    public static class SpritePivotAdjusterAPI
    {
        /// <summary>The per-axis threshold used to skip an unchanged pivot.</summary>
        public const float PivotEpsilon = 1e-3f;

        private static readonly string[] ImageFileExtensions =
        {
            ".png",
            ".jpg",
            ".jpeg",
            ".bmp",
            ".tga",
            ".psd",
            ".gif",
        };

        /// <summary>
        /// Finds texture asset paths under project folders with an optional filename filter.
        /// </summary>
        public static bool TryFind(
            IReadOnlyList<string> inputFolders,
            string spriteNameRegex,
            List<string> assetPaths,
            out string error
        )
        {
            if (assetPaths == null)
            {
                error = "A destination list is required.";
                return false;
            }

            assetPaths.Clear();
            if (inputFolders == null || inputFolders.Count == 0)
            {
                error = "At least one input folder is required.";
                return false;
            }

            Regex nameFilter;
            try
            {
                nameFilter = string.IsNullOrWhiteSpace(spriteNameRegex)
                    ? null
                    : new Regex(
                        spriteNameRegex,
                        RegexOptions.Compiled | RegexOptions.CultureInvariant,
                        TimeSpan.FromSeconds(1)
                    );
            }
            catch (ArgumentException exception)
            {
                error = $"Invalid sprite name regex: {exception.Message}";
                return false;
            }

            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            int validFolderCount = 0;
            try
            {
                foreach (string folder in inputFolders)
                {
                    string normalizedFolder = NormalizeAssetPath(folder);
                    if (
                        string.IsNullOrEmpty(normalizedFolder)
                        || !AssetDatabase.IsValidFolder(normalizedFolder)
                    )
                    {
                        continue;
                    }

                    ++validFolderCount;
                    string[] guids = AssetDatabase.FindAssets(
                        "t:Texture2D",
                        new[] { normalizedFolder }
                    );
                    foreach (string guid in guids)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        if (
                            string.IsNullOrEmpty(path)
                            || !SpriteFileExtensions.HasAny(path, ImageFileExtensions)
                            || !seen.Add(path)
                        )
                        {
                            continue;
                        }

                        string fileName = Path.GetFileNameWithoutExtension(path);
                        if (nameFilter == null || nameFilter.IsMatch(fileName))
                        {
                            assetPaths.Add(path);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                assetPaths.Clear();
                error = exception.Message;
                return false;
            }

            if (validFolderCount == 0)
            {
                error = "No valid project input folders were supplied.";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Previews or applies center-of-mass pivots to explicit sprite asset paths.
        /// </summary>
        public static Result Run(
            IReadOnlyList<string> assetPaths,
            Options options,
            bool applyChanges = false,
            Func<int, int, bool> cancelRequested = null
        )
        {
            Result result = new();
            if (assetPaths == null || options == null)
            {
                result.AddError("Asset paths and pivot options are required.");
                return result;
            }
            if (
                float.IsNaN(options.AlphaCutoff)
                || float.IsInfinity(options.AlphaCutoff)
                || options.AlphaCutoff < 0f
                || 1f < options.AlphaCutoff
            )
            {
                result.AddError("Alpha cutoff must be between zero and one.");
                return result;
            }

            result.TotalCandidates = assetPaths.Count;
            HashSet<string> processedPaths = new(StringComparer.OrdinalIgnoreCase);
            List<TextureImporter> changedImporters = new();
            AssetDatabaseBatchScope? batchScope = null;
            try
            {
                if (applyChanges)
                {
                    batchScope = AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false);
                }

                for (int index = 0; index < assetPaths.Count; ++index)
                {
                    string path = NormalizeAssetPath(assetPaths[index]);
                    if (
                        string.IsNullOrEmpty(path)
                        || !path.StartsWith("Assets/", StringComparison.Ordinal)
                    )
                    {
                        ++result.SkippedNotSprite;
                        result.AddError(
                            $"Invalid asset path at index {index}: expected a path under Assets."
                        );
                        continue;
                    }
                    if (!processedPaths.Add(path))
                    {
                        continue;
                    }

                    try
                    {
                        if (
                            !path.StartsWith("Assets/", StringComparison.Ordinal)
                            || AssetImporter.GetAtPath(path)
                                is not TextureImporter
                                {
                                    textureType: TextureImporterType.Sprite
                                } importer
                        )
                        {
                            ++result.SkippedNotSprite;
                            continue;
                        }

                        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                        if (sprite == null)
                        {
                            ++result.SkippedMissingSprite;
                            continue;
                        }

                        if (cancelRequested != null)
                        {
                            bool shouldCancel;
                            try
                            {
                                shouldCancel = cancelRequested(index, assetPaths.Count);
                            }
                            catch (Exception exception)
                            {
                                result.AddError($"Progress callback failed: {exception.Message}");
                                result.Canceled = true;
                                break;
                            }

                            if (shouldCancel)
                            {
                                result.Canceled = true;
                                break;
                            }
                        }

                        if (importer.spriteImportMode != SpriteImportMode.Single)
                        {
                            ++result.SkippedMultiSprite;
                            continue;
                        }

                        ++result.SingleSpritesProcessed;
                        if (!importer.isReadable)
                        {
                            ++result.SkippedNonReadable;
                            result.AddWarning($"Skipping non-readable texture: {path}");
                            continue;
                        }

                        Vector2 newPivot = SpritePivotAdjuster.CalculateCenterOfMassPivot(
                            sprite,
                            options.AlphaCutoff
                        );
                        Vector2 currentPivot = importer.spritePivot;
                        bool unchanged =
                            Mathf.Abs(currentPivot.x - newPivot.x) < PivotEpsilon
                            && Mathf.Abs(currentPivot.y - newPivot.y) < PivotEpsilon;
                        if (options.SkipUnchanged && !options.ForceReimport && unchanged)
                        {
                            ++result.SkippedUnchanged;
                            continue;
                        }

                        if (applyChanges)
                        {
                            Undo.RecordObject(importer, "Adjust Sprite Pivot");
                            TextureImporterSettings settings = new();
                            importer.ReadTextureSettings(settings);
                            settings.spritePivot = newPivot;
                            settings.spriteAlignment = (int)SpriteAlignment.Custom;
                            importer.SetTextureSettings(settings);
                            importer.spritePivot = newPivot;
                            Undo.FlushUndoRecordObjects();
                            changedImporters.Add(importer);
                        }

                        ++result.Changed;
                    }
                    catch (Exception exception)
                    {
                        result.AddError($"Failed to process '{path}': {exception.Message}");
                    }
                }
            }
            catch (Exception exception)
            {
                result.AddError($"Failed to adjust sprite pivots: {exception.Message}");
            }
            finally
            {
                try
                {
                    batchScope?.Dispose();
                }
                catch (Exception exception)
                {
                    result.AddError($"Failed to finish asset batch: {exception.Message}");
                }

                if (applyChanges && changedImporters.Count != 0)
                {
                    foreach (TextureImporter importer in changedImporters)
                    {
                        try
                        {
                            importer.SaveAndReimport();
                        }
                        catch (Exception exception)
                        {
                            result.AddError(
                                $"Failed to reimport '{importer.assetPath}': {exception.Message}"
                            );
                        }
                    }

                    try
                    {
                        AssetDatabase.SaveAssets();
                        AssetDatabase.Refresh();
                    }
                    catch (Exception exception)
                    {
                        result.AddError($"Failed to save adjusted sprites: {exception.Message}");
                    }
                }
            }

            return result;
        }

        private static string NormalizeAssetPath(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return null;
            }

            string normalized = input.Trim().Replace('\\', '/').TrimEnd('/');
            if (string.Equals(normalized, "Assets", StringComparison.OrdinalIgnoreCase))
            {
                return "Assets";
            }
            return normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                ? "Assets" + normalized.Substring("Assets".Length)
                : null;
        }

        /// <summary>
        /// Controls center-of-mass pivot calculation and reimport behavior.
        /// </summary>
        public sealed class Options
        {
            /// <summary>Gets or sets the alpha cutoff, from zero to one.</summary>
            public float AlphaCutoff { get; set; } = 0.01f;

            /// <summary>Gets or sets whether unchanged pivots are skipped.</summary>
            public bool SkipUnchanged { get; set; } = true;

            /// <summary>Gets or sets whether unchanged sprites are reimported.</summary>
            public bool ForceReimport { get; set; }
        }

        /// <summary>
        /// Reports pivot adjustment counts and diagnostics.
        /// </summary>
        public sealed class Result
        {
            /// <summary>Gets the number of supplied paths.</summary>
            public int TotalCandidates { get; internal set; }

            /// <summary>Gets the number of single sprites processed.</summary>
            public int SingleSpritesProcessed { get; internal set; }

            /// <summary>Gets the number of pivots changed or that would change.</summary>
            public int Changed { get; internal set; }

            /// <summary>Gets the number of unchanged pivots skipped.</summary>
            public int SkippedUnchanged { get; internal set; }

            /// <summary>Gets the number of non-readable textures skipped.</summary>
            public int SkippedNonReadable { get; internal set; }

            /// <summary>Gets the number of non-sprite textures skipped.</summary>
            public int SkippedNotSprite { get; internal set; }

            /// <summary>Gets the number of missing sprite assets skipped.</summary>
            public int SkippedMissingSprite { get; internal set; }

            /// <summary>Gets the number of multi-sprite textures skipped.</summary>
            public int SkippedMultiSprite { get; internal set; }

            /// <summary>Gets whether the operation was canceled.</summary>
            public bool Canceled { get; internal set; }

            /// <summary>Gets errors encountered while processing.</summary>
            public IReadOnlyList<string> Errors => _errors;

            /// <summary>Gets nonfatal warnings encountered while processing.</summary>
            public IReadOnlyList<string> Warnings => _warnings;

            private readonly List<string> _errors = new();
            private readonly List<string> _warnings = new();

            internal void AddError(string error)
            {
                _errors.Add(error);
            }

            internal void AddWarning(string warning)
            {
                _warnings.Add(warning);
            }
        }
    }
#endif
}
