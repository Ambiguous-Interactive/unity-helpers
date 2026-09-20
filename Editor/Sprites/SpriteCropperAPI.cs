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
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Discovers sprite textures for cropping without opening an editor window.
    /// </summary>
    public static class SpriteCropperAPI
    {
        private const string CroppedPrefix = "Cropped_";

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
        /// Finds single-sprite images and separately reports multi-sprite images under project folders.
        /// </summary>
        public static bool TryFind(
            IReadOnlyList<string> inputFolders,
            string spriteNameRegex,
            List<string> singleSpritePaths,
            List<string> multiSpritePaths,
            out string error
        )
        {
            if (
                singleSpritePaths == null
                || multiSpritePaths == null
                || ReferenceEquals(singleSpritePaths, multiSpritePaths)
            )
            {
                error = "Separate output lists are required.";
                return false;
            }

            singleSpritePaths.Clear();
            multiSpritePaths.Clear();
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

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                error = "The Unity project root could not be found.";
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
                    string absoluteFolder = ToFullPath(normalizedFolder);
                    string[] files = Directory.GetFiles(
                        absoluteFolder,
                        "*.*",
                        SearchOption.AllDirectories
                    );
                    foreach (string absoluteFile in files)
                    {
                        string assetPath = absoluteFile
                            .Substring(projectRoot.Length + 1)
                            .Replace('\\', '/');
                        if (
                            !seen.Add(assetPath)
                            || !SpriteFileExtensions.HasAny(assetPath, ImageFileExtensions)
                            || assetPath.Contains(CroppedPrefix, StringComparison.OrdinalIgnoreCase)
                        )
                        {
                            continue;
                        }

                        string fileName = Path.GetFileNameWithoutExtension(assetPath);
                        if (nameFilter != null && !nameFilter.IsMatch(fileName))
                        {
                            continue;
                        }

                        if (
                            AssetImporter.GetAtPath(assetPath)
                            is not TextureImporter
                            {
                                textureType: TextureImporterType.Sprite
                            } importer
                        )
                        {
                            continue;
                        }
                        if (importer.spriteImportMode != SpriteImportMode.Single)
                        {
                            multiSpritePaths.Add(assetPath);
                            continue;
                        }

                        singleSpritePaths.Add(assetPath);
                    }
                }
            }
            catch (Exception exception)
            {
                singleSpritePaths.Clear();
                multiSpritePaths.Clear();
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
        /// Crops one project sprite and restores its source readability after writing a separate output.
        /// </summary>
        public static CropResult Crop(string assetPath, CropOptions options)
        {
            if (options == null)
            {
                return new CropResult(CropStatus.FatalError, null, "Crop options are required.");
            }

            assetPath = NormalizeAssetPath(assetPath);
            if (
                string.IsNullOrEmpty(assetPath)
                || string.Equals(assetPath, "Assets", StringComparison.Ordinal)
            )
            {
                return new CropResult(
                    CropStatus.FatalError,
                    null,
                    "A project sprite path is required."
                );
            }

            bool sourceWasReadable = true;
            CropResult result = null;
            try
            {
                TextureImporter sourceImporter =
                    AssetImporter.GetAtPath(assetPath) as TextureImporter;
                if (
                    sourceImporter == null
                    || sourceImporter.textureType != TextureImporterType.Sprite
                )
                {
                    result = new CropResult(
                        CropStatus.FatalError,
                        null,
                        "The source must be an imported sprite texture."
                    );
                    return result;
                }

                sourceWasReadable = sourceImporter.isReadable;
                if (!sourceWasReadable)
                {
                    sourceImporter.isReadable = true;
                    sourceImporter.SaveAndReimport();
                }

                result = CropPrepared(assetPath, options, sourceWasReadable);
                if (result.Status == CropStatus.Success && result.OutputImporter != null)
                {
                    result.OutputImporter.SaveAndReimport();
                    AssetDatabase.SaveAssets();
                }
                return result;
            }
            catch (Exception exception)
            {
                result = new CropResult(CropStatus.RetryableError, null, exception.Message);
                return result;
            }
            finally
            {
                if (
                    !sourceWasReadable
                    && (
                        !options.OverwriteOriginals
                        || result == null
                        || result.Status != CropStatus.Success
                    )
                )
                {
                    try
                    {
                        TextureImporter currentSource =
                            AssetImporter.GetAtPath(assetPath) as TextureImporter;
                        if (currentSource != null && currentSource.isReadable)
                        {
                            currentSource.isReadable = false;
                            currentSource.SaveAndReimport();
                        }
                    }
                    catch (Exception exception)
                    {
                        if (result != null)
                        {
                            result.Status = CropStatus.RetryableError;
                            result.Error =
                                $"Failed to restore source readability: {exception.Message}";
                        }
                        Debug.LogError(
                            $"Failed to restore sprite readability for '{assetPath}': {exception.Message}"
                        );
                    }
                }
            }
        }

        /// <summary>
        /// Maps original sprites to their cropped counterparts under the supplied project folders.
        /// </summary>
        public static bool TryBuildReplacementMap(
            IReadOnlyList<string> inputFolders,
            Dictionary<Sprite, Sprite> replacements,
            out string error,
            string outputFolder = null
        )
        {
            if (replacements == null)
            {
                error = "A sprite replacement map is required.";
                return false;
            }

            replacements.Clear();
            if (!string.IsNullOrWhiteSpace(outputFolder))
            {
                string normalizedOutputFolder = NormalizeAssetPath(outputFolder);
                if (string.IsNullOrEmpty(normalizedOutputFolder))
                {
                    error = $"Invalid output folder: '{outputFolder}'.";
                    return false;
                }
                outputFolder = normalizedOutputFolder;
            }
            if (
                !string.IsNullOrWhiteSpace(outputFolder)
                && (!IsAssetsFolder(outputFolder) || !AssetDatabase.IsValidFolder(outputFolder))
            )
            {
                error = $"Invalid output folder: '{outputFolder}'.";
                return false;
            }
            List<string> sourcePaths = new();
            List<string> multiSpritePaths = new();
            if (!TryFind(inputFolders, null, sourcePaths, multiSpritePaths, out error))
            {
                return false;
            }

            try
            {
                foreach (string sourcePath in sourcePaths)
                {
                    string directory = Path.GetDirectoryName(sourcePath)?.Replace('\\', '/');
                    if (string.IsNullOrWhiteSpace(directory))
                    {
                        continue;
                    }
                    string croppedDirectory = string.IsNullOrWhiteSpace(outputFolder)
                        ? directory
                        : outputFolder.TrimEnd('/');
                    string croppedPath =
                        $"{croppedDirectory}/{CroppedPrefix}{Path.GetFileName(sourcePath)}";
                    if (!File.Exists(ToFullPath(croppedPath)))
                    {
                        continue;
                    }

                    Sprite original = AssetDatabase.LoadAssetAtPath<Sprite>(sourcePath);
                    Sprite cropped = AssetDatabase.LoadAssetAtPath<Sprite>(croppedPath);
                    if (original != null && cropped != null)
                    {
                        replacements[original] = cropped;
                    }
                }
            }
            catch (Exception exception)
            {
                replacements.Clear();
                error = exception.Message;
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Previews or replaces references to original sprites with separate cropped outputs.
        /// </summary>
        public static SpriteReferenceReplacementResult ReplaceReferences(
            IReadOnlyList<string> inputFolders,
            bool applyChanges = false,
            Func<int, int, bool> cancelRequested = null,
            string outputFolder = null,
            bool overwriteOriginals = false
        )
        {
            SpriteReferenceReplacementResult emptyResult = new();
            if (overwriteOriginals)
            {
                emptyResult.AddError(
                    "Reference replacement requires separate Cropped_* outputs; overwritten sprites keep their original asset paths."
                );
                return emptyResult;
            }

            try
            {
                Dictionary<Sprite, Sprite> replacements = new();
                if (
                    !TryBuildReplacementMap(
                        inputFolders,
                        replacements,
                        out string error,
                        outputFolder
                    )
                )
                {
                    emptyResult.AddError(error);
                    return emptyResult;
                }
                if (replacements.Count == 0)
                {
                    emptyResult.AddError("No original and cropped sprite pairs were found.");
                    return emptyResult;
                }

                return SpriteSheetReferenceReplacementAPI.Run(
                    replacements,
                    AssetDatabase.GetAllAssetPaths(),
                    applyChanges,
                    cancelRequested
                );
            }
            catch (Exception exception)
            {
                emptyResult.AddError($"Sprite reference replacement failed: {exception.Message}");
                return emptyResult;
            }
        }

        internal static CropResult CropPrepared(
            string assetPath,
            CropOptions options,
            bool sourceWasReadable
        )
        {
            assetPath = NormalizeAssetPath(assetPath);
            if (
                options == null
                || string.IsNullOrEmpty(assetPath)
                || !assetPath.StartsWith("Assets/", StringComparison.Ordinal)
                || options.LeftPadding < 0
                || options.RightPadding < 0
                || options.TopPadding < 0
                || options.BottomPadding < 0
                || (
                    options.OutputReadability != OutputReadability.MirrorSource
                    && options.OutputReadability != OutputReadability.Readable
                    && options.OutputReadability != OutputReadability.NotReadable
                )
            )
            {
                return new CropResult(
                    CropStatus.FatalError,
                    null,
                    "A project sprite path and nonnegative padding are required."
                );
            }

            string assetDirectory = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(assetDirectory))
            {
                return new CropResult(CropStatus.FatalError, null, "The sprite has no directory.");
            }

            string outputDirectory = assetDirectory;
            if (!options.OverwriteOriginals && !string.IsNullOrWhiteSpace(options.OutputFolder))
            {
                string normalizedOutputFolder = NormalizeAssetPath(options.OutputFolder);
                if (
                    string.IsNullOrEmpty(normalizedOutputFolder)
                    || !AssetDatabase.IsValidFolder(normalizedOutputFolder)
                )
                {
                    return new CropResult(
                        CropStatus.FatalError,
                        null,
                        $"Invalid output folder: '{options.OutputFolder}'."
                    );
                }
                outputDirectory = normalizedOutputFolder;
            }

            if (
                AssetImporter.GetAtPath(assetPath)
                is not TextureImporter { textureType: TextureImporterType.Sprite } importer
            )
            {
                return new CropResult(
                    CropStatus.FatalError,
                    null,
                    $"No sprite texture importer exists at '{assetPath}'."
                );
            }
            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                return new CropResult(CropStatus.SkippedNoChange, null, null);
            }

            try
            {
                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                if (texture == null)
                {
                    return new CropResult(
                        CropStatus.RetryableError,
                        null,
                        $"The texture at '{assetPath}' could not be loaded."
                    );
                }

                Color32[] pixels = texture.GetPixels32();
                int width = texture.width;
                int height = texture.height;
                SpriteCropper.CropComputation crop = SpriteCropper.ComputeCrop(
                    pixels,
                    width,
                    height,
                    options.LeftPadding,
                    options.RightPadding,
                    options.TopPadding,
                    options.BottomPadding,
                    SpriteCropper.AlphaThreshold,
                    importer.spritePivot,
                    options.OnlyNecessary
                );
                if (crop.ShouldSkipNoChange)
                {
                    return new CropResult(CropStatus.SkippedNoChange, null, null);
                }

                TextureImporterSettings sourceSettings = new();
                importer.ReadTextureSettings(sourceSettings);
                TextureImporterType sourceType = importer.textureType;
                FilterMode sourceFilter = importer.filterMode;
                TextureImporterCompression sourceCompression = importer.textureCompression;
                TextureWrapMode sourceWrap = importer.wrapMode;
                bool sourceMipmaps = importer.mipmapEnabled;
                float sourcePixelsPerUnit = importer.spritePixelsPerUnit;
                TextureImporterPlatformSettings defaultPlatform = null;
                string warning = null;
                if (options.CopyDefaultPlatformSettings)
                {
                    try
                    {
                        defaultPlatform = importer.GetDefaultPlatformTextureSettings();
                    }
                    catch (Exception exception)
                    {
                        warning = $"Default platform settings were not copied: {exception.Message}";
                    }
                }

                string outputName = options.OverwriteOriginals
                    ? Path.GetFileName(assetPath)
                    : CroppedPrefix + Path.GetFileName(assetPath);
                string outputPath = $"{outputDirectory}/{outputName}";
                Texture2D cropped = new(
                    crop.CropWidth,
                    crop.CropHeight,
                    TextureFormat.RGBA32,
                    false
                );
                try
                {
                    int pixelCount = crop.CropWidth * crop.CropHeight;
                    using PooledArray<Color32> pixelLease = SystemArrayPool<Color32>.Get(
                        pixelCount,
                        out Color32[] croppedPixels
                    );
                    SpriteCropper.CopyCropPixels(
                        pixels,
                        width,
                        height,
                        crop.VisibleMinX,
                        crop.VisibleMinY,
                        crop.VisibleMaxX,
                        crop.VisibleMaxY,
                        crop.CropWidth,
                        crop.CropHeight,
                        croppedPixels
                    );
                    cropped.SetPixels32(0, 0, crop.CropWidth, crop.CropHeight, croppedPixels, 0);
                    cropped.Apply();
                    File.WriteAllBytes(ToFullPath(outputPath), cropped.EncodeToPNG());
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(cropped);
                }

                AssetDatabase.ImportAsset(outputPath);
                TextureImporter outputImporter =
                    AssetImporter.GetAtPath(outputPath) as TextureImporter;
                if (outputImporter == null)
                {
                    return new CropResult(
                        CropStatus.RetryableError,
                        outputPath,
                        $"The cropped texture at '{outputPath}' could not be imported."
                    );
                }

                Vector4 border = sourceSettings.spriteBorder;
                border.x = Mathf.Max(0, border.x - crop.VisibleMinX);
                border.y = Mathf.Max(0, border.y - crop.VisibleMinY);
                border.z = Mathf.Max(0, border.z - (width - 1 - crop.VisibleMaxX));
                border.w = Mathf.Max(0, border.w - (height - 1 - crop.VisibleMaxY));
                sourceSettings.spritePivot = crop.NewPivot;
                sourceSettings.spriteAlignment = (int)SpriteAlignment.Custom;
                sourceSettings.spriteBorder = border;
                outputImporter.SetTextureSettings(sourceSettings);
                outputImporter.spriteImportMode = SpriteImportMode.Single;
                outputImporter.spritePivot = crop.NewPivot;
                outputImporter.textureType = sourceType;
                outputImporter.filterMode = sourceFilter;
                outputImporter.textureCompression = sourceCompression;
                outputImporter.wrapMode = sourceWrap;
                outputImporter.mipmapEnabled = sourceMipmaps;
                outputImporter.spritePixelsPerUnit = sourcePixelsPerUnit;
                if (defaultPlatform != null && !string.IsNullOrWhiteSpace(defaultPlatform.name))
                {
                    try
                    {
                        outputImporter.SetPlatformTextureSettings(defaultPlatform);
                    }
                    catch (Exception exception)
                    {
                        warning = $"Default platform settings were not copied: {exception.Message}";
                    }
                }
                switch (options.OutputReadability)
                {
                    case OutputReadability.MirrorSource:
                        outputImporter.isReadable = sourceWasReadable;
                        break;
                    case OutputReadability.Readable:
                        outputImporter.isReadable = true;
                        break;
                    case OutputReadability.NotReadable:
                        outputImporter.isReadable = false;
                        break;
                }

                return new CropResult(CropStatus.Success, outputPath, null)
                {
                    OutputImporter = outputImporter,
                    Warning = warning,
                };
            }
            catch (Exception exception)
            {
                return new CropResult(
                    CropStatus.RetryableError,
                    null,
                    $"Failed to crop '{assetPath}': {exception.Message}"
                );
            }
        }

        private static bool IsAssetsFolder(string folder)
        {
            return string.Equals(folder, "Assets", StringComparison.Ordinal)
                || folder.StartsWith("Assets/", StringComparison.Ordinal);
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

        private static string ToFullPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string fullPath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
            string assetsRoot = Path.GetFullPath(Application.dataPath);
            if (
                !string.Equals(fullPath, assetsRoot, StringComparison.OrdinalIgnoreCase)
                && !fullPath.StartsWith(
                    assetsRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                throw new InvalidOperationException("Asset paths must remain under Assets.");
            }
            return fullPath;
        }

        /// <summary>
        /// Options for cropping a single-sprite texture.
        /// </summary>
        public sealed class CropOptions
        {
            /// <summary>Gets or sets the left padding in pixels.</summary>
            public int LeftPadding { get; set; }

            /// <summary>Gets or sets the right padding in pixels.</summary>
            public int RightPadding { get; set; }

            /// <summary>Gets or sets the top padding in pixels.</summary>
            public int TopPadding { get; set; }

            /// <summary>Gets or sets the bottom padding in pixels.</summary>
            public int BottomPadding { get; set; }

            /// <summary>Gets or sets whether unchanged sprites are skipped.</summary>
            public bool OnlyNecessary { get; set; }

            /// <summary>Gets or sets whether the source file is overwritten.</summary>
            public bool OverwriteOriginals { get; set; }

            /// <summary>Gets or sets the optional Assets output folder.</summary>
            public string OutputFolder { get; set; }

            /// <summary>Gets or sets the output importer readability.</summary>
            public OutputReadability OutputReadability { get; set; }

            /// <summary>Gets or sets whether default platform settings are copied.</summary>
            public bool CopyDefaultPlatformSettings { get; set; } = true;
        }

        /// <summary>
        /// Reports the outcome of cropping one sprite.
        /// </summary>
        public sealed class CropResult
        {
            /// <summary>Gets the outcome of the operation.</summary>
            public CropStatus Status { get; internal set; }

            /// <summary>Gets the output asset path when a file was written.</summary>
            public string OutputPath { get; }

            /// <summary>Gets the error when cropping failed.</summary>
            public string Error { get; internal set; }

            /// <summary>Gets a nonfatal importer-settings warning.</summary>
            public string Warning { get; internal set; }

            internal TextureImporter OutputImporter { get; set; }

            internal CropResult(CropStatus status, string outputPath, string error)
            {
                Status = status;
                OutputPath = outputPath;
                Error = error;
            }
        }

        /// <summary>
        /// Indicates whether a sprite was cropped, skipped, or could not be processed.
        /// </summary>
        public enum CropStatus
        {
            Success = 0,
            SkippedNoChange = 1,
            RetryableError = 2,
            FatalError = 3,
        }

        /// <summary>
        /// Selects the output texture's read/write setting.
        /// </summary>
        public enum OutputReadability
        {
            MirrorSource = 0,
            Readable = 1,
            NotReadable = 2,
        }
    }
#endif
}
