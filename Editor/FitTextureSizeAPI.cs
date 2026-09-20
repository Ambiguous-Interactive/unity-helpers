// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.RegularExpressions;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Utils;

    public enum FitMode
    {
        GrowAndShrink = 0,
        GrowOnly = 1,
        ShrinkOnly = 2,
        RoundToNearest = 3,
    }

    /// <summary>
    /// Finds textures and fits their importer size without opening an editor window.
    /// </summary>
    public static class FitTextureSizeAPI
    {
        /// <summary>
        /// Finds texture GUIDs under the supplied asset paths, or under Assets when none are supplied.
        /// </summary>
        public static bool TryFindTextures(
            IReadOnlyList<string> sourcePaths,
            bool onlySprites,
            List<string> destination,
            out string error,
            bool searchAssetsWhenEmpty = true
        )
        {
            if (destination == null)
            {
                error = "A destination list is required.";
                return false;
            }

            destination.Clear();
            try
            {
                using PooledResource<HashSet<string>> folderLease = Buffers<string>.HashSet.Get(
                    out HashSet<string> folders
                );
                using PooledResource<HashSet<string>> guidLease = Buffers<string>.HashSet.Get(
                    out HashSet<string> guids
                );

                if (sourcePaths != null)
                {
                    foreach (string path in sourcePaths)
                    {
                        if (string.IsNullOrWhiteSpace(path))
                        {
                            continue;
                        }

                        if (AssetDatabase.IsValidFolder(path))
                        {
                            folders.Add(path);
                            continue;
                        }

                        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                        if (
                            importer != null
                            && (!onlySprites || importer.textureType == TextureImporterType.Sprite)
                        )
                        {
                            string guid = AssetDatabase.AssetPathToGUID(path);
                            if (!string.IsNullOrWhiteSpace(guid))
                            {
                                guids.Add(guid);
                            }
                        }
                    }
                }

                if (
                    searchAssetsWhenEmpty
                    && folders.Count == 0
                    && (sourcePaths == null || sourcePaths.Count == 0)
                )
                {
                    folders.Add("Assets");
                }

                if (0 < folders.Count)
                {
                    string[] folderPaths = new string[folders.Count];
                    folders.CopyTo(folderPaths);
                    string[] found = AssetDatabase.FindAssets(
                        onlySprites ? "t:sprite" : "t:texture2D",
                        folderPaths
                    );
                    foreach (string guid in found)
                    {
                        guids.Add(guid);
                    }
                }

                if (destination.Capacity < guids.Count)
                {
                    destination.Capacity = guids.Count;
                }
                foreach (string guid in guids)
                {
                    destination.Add(guid);
                }
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                destination.Clear();
                error = exception.Message;
                return false;
            }
        }

        /// <summary>
        /// Calculates or applies size changes to explicitly supplied texture GUIDs.
        /// </summary>
        public static Result Run(
            IReadOnlyList<string> textureGuids,
            Options options,
            bool applyChanges,
            Func<string, int, int, bool> cancelRequested = null
        )
        {
            if (textureGuids == null || options == null)
            {
                return new Result(
                    false,
                    false,
                    "Texture GUIDs and options are required.",
                    0,
                    0,
                    0,
                    0,
                    0
                );
            }
            if (
                options.MinAllowedTextureSize < 1
                || options.MaxAllowedTextureSize < options.MinAllowedTextureSize
                || 16384 < options.MaxAllowedTextureSize
            )
            {
                return new Result(false, false, "Texture size bounds are invalid.", 0, 0, 0, 0, 0);
            }
            if (
                options.FitMode != FitMode.GrowAndShrink
                && options.FitMode != FitMode.GrowOnly
                && options.FitMode != FitMode.ShrinkOnly
                && options.FitMode != FitMode.RoundToNearest
            )
            {
                return new Result(false, false, "Fit mode is invalid.", 0, 0, 0, 0, 0);
            }

            int changed = 0;
            int grown = 0;
            int shrunk = 0;
            int unchanged = 0;
            bool cancelled = false;
            string error = string.Empty;
            string currentPath = string.Empty;
            try
            {
                Regex nameRegex = null;
                bool hasNameFilter = !string.IsNullOrWhiteSpace(options.NameFilter);
                if (hasNameFilter && options.UseRegexForName)
                {
                    nameRegex = new Regex(
                        options.NameFilter,
                        options.CaseSensitiveNameFilter
                            ? RegexOptions.None
                            : RegexOptions.IgnoreCase,
                        TimeSpan.FromSeconds(1)
                    );
                }

                using PooledResource<HashSet<string>> labelLease = Buffers<string>.HashSet.Get(
                    out HashSet<string> labels
                );
                if (!string.IsNullOrWhiteSpace(options.LabelFilterCsv))
                {
                    string[] parts = options.LabelFilterCsv.Split(
                        new[] { ',', ';' },
                        StringSplitOptions.RemoveEmptyEntries
                    );
                    foreach (string part in parts)
                    {
                        string label = part.Trim();
                        if (!string.IsNullOrEmpty(label))
                        {
                            labels.Add(
                                options.CaseSensitiveNameFilter ? label : label.ToLowerInvariant()
                            );
                        }
                    }
                }

                AssetDatabaseBatchScope batch = applyChanges
                    ? AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false)
                    : default;
                try
                {
                    for (int index = 0; index < textureGuids.Count; index++)
                    {
                        string guid = textureGuids[index];
                        string path = string.IsNullOrWhiteSpace(guid)
                            ? string.Empty
                            : AssetDatabase.GUIDToAssetPath(guid);
                        currentPath = path;
                        if (
                            cancelRequested != null
                            && (index % 32 == 0 || index == textureGuids.Count - 1)
                            && cancelRequested(path, index + 1, textureGuids.Count)
                        )
                        {
                            cancelled = true;
                            break;
                        }
                        if (string.IsNullOrWhiteSpace(path))
                        {
                            continue;
                        }

                        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                        if (
                            importer == null
                            || (
                                options.OnlySprites
                                && importer.textureType != TextureImporterType.Sprite
                            )
                        )
                        {
                            continue;
                        }
                        if (hasNameFilter && !MatchesName(path, options, nameRegex))
                        {
                            continue;
                        }
                        if (
                            0 < labels.Count
                            && !MatchesLabel(path, labels, options.CaseSensitiveNameFilter)
                        )
                        {
                            continue;
                        }

                        importer.GetSourceTextureWidthAndHeight(out int width, out int height);
                        FitComputation fit = ComputeFit(
                            width,
                            height,
                            importer.maxTextureSize,
                            options.FitMode,
                            options.MinAllowedTextureSize,
                            options.MaxAllowedTextureSize
                        );
                        if (!fit.NeedsChange || importer.maxTextureSize == fit.TargetSize)
                        {
                            unchanged++;
                            continue;
                        }

                        changed++;
                        if (fit.Grew)
                        {
                            grown++;
                        }
                        if (fit.Shrank)
                        {
                            shrunk++;
                        }
                        if (!applyChanges)
                        {
                            continue;
                        }

                        Undo.RecordObject(importer, "Fit Texture Size");
                        importer.maxTextureSize = fit.TargetSize;
                        ApplyPlatformOverride(
                            importer,
                            "Standalone",
                            fit.TargetSize,
                            options.ApplyToStandalone,
                            options
                        );
                        ApplyPlatformOverride(
                            importer,
                            "Android",
                            fit.TargetSize,
                            options.ApplyToAndroid,
                            options
                        );
                        ApplyPlatformOverride(
                            importer,
                            "iPhone",
                            fit.TargetSize,
                            options.ApplyToiOS,
                            options
                        );
                        AssetDatabase.WriteImportSettingsIfDirty(path);
                    }
                }
                finally
                {
                    if (applyChanges)
                    {
                        batch.Dispose();
                        if (0 < changed)
                        {
                            AssetDatabase.SaveAssets();
                            AssetDatabase.Refresh();
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                error = string.IsNullOrEmpty(currentPath)
                    ? exception.Message
                    : $"{currentPath}: {exception.Message}";
            }

            return new Result(
                string.IsNullOrEmpty(error) && !cancelled,
                cancelled,
                error,
                textureGuids.Count,
                changed,
                grown,
                shrunk,
                unchanged
            );
        }

        internal static FitComputation ComputeFit(
            int width,
            int height,
            int currentTextureSize,
            FitMode fitMode,
            int minAllowedTextureSize,
            int maxAllowedTextureSize
        )
        {
            int targetTextureSize = currentTextureSize;
            bool needsChange = false;
            bool grew = false;
            bool shrank = false;

            if (fitMode == FitMode.RoundToNearest)
            {
                int largest = Mathf.Max(width, height);
                int upper = Mathf.NextPowerOfTwo(Mathf.Max(largest, 1));
                int lower = upper == largest ? upper : (upper >> 1);
                int diffDown = largest - lower;
                int diffUp = upper - largest;
                int nearest = diffDown < diffUp ? lower : upper;
                if (nearest != targetTextureSize)
                {
                    targetTextureSize = nearest;
                    needsChange = true;
                }
            }
            else if (fitMode == FitMode.GrowAndShrink)
            {
                int largest = Mathf.Max(width, height);
                int target = Mathf.NextPowerOfTwo(Mathf.Max(largest, 1));
                if (currentTextureSize != target)
                {
                    targetTextureSize = target;
                    needsChange = true;
                }
            }
            else if (fitMode == FitMode.GrowOnly)
            {
                int size = Mathf.Max(width, height);
                int tempSize = Mathf.Max(targetTextureSize, 1);
                while (tempSize < size && tempSize < maxAllowedTextureSize && tempSize < (1 << 30))
                {
                    tempSize <<= 1;
                }
                if (tempSize != targetTextureSize)
                {
                    targetTextureSize = tempSize;
                    needsChange = true;
                }
            }
            else if (fitMode == FitMode.ShrinkOnly)
            {
                int size = Mathf.Max(width, height);
                int neededPot = Mathf.NextPowerOfTwo(Mathf.Max(size, 1));
                int tempSize = targetTextureSize;

                if (neededPot < tempSize)
                {
                    tempSize = neededPot;
                }
                if (tempSize != targetTextureSize)
                {
                    targetTextureSize = tempSize;
                    needsChange = true;
                }
            }

            if (targetTextureSize < minAllowedTextureSize)
            {
                targetTextureSize = minAllowedTextureSize;
                needsChange = needsChange || (currentTextureSize != targetTextureSize);
            }
            if (maxAllowedTextureSize < targetTextureSize)
            {
                targetTextureSize = maxAllowedTextureSize;
                needsChange = needsChange || (currentTextureSize != targetTextureSize);
            }

            if (needsChange)
            {
                grew = currentTextureSize < targetTextureSize;
                shrank = targetTextureSize < currentTextureSize;
            }

            return new FitComputation(targetTextureSize, needsChange, grew, shrank);
        }

        private static bool MatchesName(string path, Options options, Regex regex)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (regex != null)
            {
                return regex.IsMatch(name);
            }
            return 0
                <= name.IndexOf(
                    options.NameFilter,
                    options.CaseSensitiveNameFilter
                        ? StringComparison.Ordinal
                        : StringComparison.OrdinalIgnoreCase
                );
        }

        private static bool MatchesLabel(
            string path,
            HashSet<string> requiredLabels,
            bool caseSensitive
        )
        {
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null)
            {
                return false;
            }
            string[] labels = AssetDatabase.GetLabels(asset);
            foreach (string label in labels)
            {
                if (requiredLabels.Contains(caseSensitive ? label : label.ToLowerInvariant()))
                {
                    return true;
                }
            }
            return false;
        }

        private static void ApplyPlatformOverride(
            TextureImporter importer,
            string platform,
            int target,
            bool enabled,
            Options options
        )
        {
            if (!enabled)
            {
                return;
            }
            TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(
                platform
            );
            settings.overridden = true;
            settings.maxTextureSize = Mathf.Clamp(
                target,
                options.MinAllowedTextureSize,
                options.MaxAllowedTextureSize
            );
            importer.SetPlatformTextureSettings(settings);
        }

        /// <summary>
        /// Pure result of the fit-size computation (no Unity asset I/O), so the
        /// power-of-two / clamp / direction behavior can be unit-tested without importing
        /// textures.
        /// </summary>
        internal readonly struct FitComputation
        {
            public readonly int TargetSize;
            public readonly bool NeedsChange;
            public readonly bool Grew;
            public readonly bool Shrank;

            public FitComputation(int targetSize, bool needsChange, bool grew, bool shrank)
            {
                TargetSize = targetSize;
                NeedsChange = needsChange;
                Grew = grew;
                Shrank = shrank;
            }
        }

        /// <summary>
        /// Supplies texture-fit settings without editor-window state.
        /// </summary>
        public sealed class Options
        {
            /// <summary>Controls how the target size is chosen.</summary>
            public FitMode FitMode { get; set; } = FitMode.GrowAndShrink;

            /// <summary>Limits the run to sprite importers.</summary>
            public bool OnlySprites { get; set; }

            /// <summary>Sets the minimum importer size.</summary>
            public int MinAllowedTextureSize { get; set; } = 32;

            /// <summary>Sets the maximum importer size.</summary>
            public int MaxAllowedTextureSize { get; set; } = 8192;

            /// <summary>Applies a Standalone platform override.</summary>
            public bool ApplyToStandalone { get; set; }

            /// <summary>Applies an Android platform override.</summary>
            public bool ApplyToAndroid { get; set; }

            /// <summary>Applies an iOS platform override.</summary>
            public bool ApplyToiOS { get; set; }

            /// <summary>Filters texture names.</summary>
            public string NameFilter { get; set; } = string.Empty;

            /// <summary>Interprets the name filter as a regular expression.</summary>
            public bool UseRegexForName { get; set; }

            /// <summary>Uses case-sensitive name and label matching.</summary>
            public bool CaseSensitiveNameFilter { get; set; }

            /// <summary>Filters assets by comma- or semicolon-separated labels.</summary>
            public string LabelFilterCsv { get; set; } = string.Empty;
        }

        /// <summary>
        /// Describes an attempted texture fit, including partial changes after failure.
        /// </summary>
        public readonly struct Result
        {
            /// <summary>Indicates whether the operation completed.</summary>
            public bool Succeeded { get; }

            /// <summary>Indicates whether the caller cancelled the operation.</summary>
            public bool Cancelled { get; }

            /// <summary>Describes a failure when the operation did not complete.</summary>
            public string Error { get; }

            /// <summary>Counts supplied texture GUIDs.</summary>
            public int Total { get; }

            /// <summary>Counts textures that need or received a size change.</summary>
            public int Changed { get; }

            /// <summary>Counts textures whose target size grew.</summary>
            public int Grown { get; }

            /// <summary>Counts textures whose target size shrank.</summary>
            public int Shrunk { get; }

            /// <summary>Counts textures whose size remained unchanged.</summary>
            public int Unchanged { get; }

            internal Result(
                bool succeeded,
                bool cancelled,
                string error,
                int total,
                int changed,
                int grown,
                int shrunk,
                int unchanged
            )
            {
                Succeeded = succeeded;
                Cancelled = cancelled;
                Error = error;
                Total = total;
                Changed = changed;
                Grown = grown;
                Shrunk = shrunk;
                Unchanged = unchanged;
            }
        }
    }
#endif
}
