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
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Describes one sprite to extract from a project texture.
    /// </summary>
    public sealed class SpriteSheetExtractionRequest
    {
        /// <summary>Gets the source texture asset path.</summary>
        public string SourceAssetPath { get; }

        /// <summary>Gets the destination PNG asset path.</summary>
        public string OutputAssetPath { get; }

        /// <summary>Gets the source pixel rectangle.</summary>
        public Rect SourceRect { get; }

        /// <summary>Gets the normalized output pivot.</summary>
        public Vector2 Pivot { get; }

        /// <summary>Gets the output sprite border.</summary>
        public Vector4 Border { get; }

        /// <summary>Creates an explicit extraction request.</summary>
        public SpriteSheetExtractionRequest(
            string sourceAssetPath,
            string outputAssetPath,
            Rect sourceRect,
            Vector2 pivot,
            Vector4 border
        )
        {
            SourceAssetPath = sourceAssetPath;
            OutputAssetPath = outputAssetPath;
            SourceRect = sourceRect;
            Pivot = pivot;
            Border = border;
        }
    }

    /// <summary>
    /// Reports the outcome of a sprite extraction batch.
    /// </summary>
    public sealed class SpriteSheetExtractionResult
    {
        /// <summary>Gets the number of sprites written or planned.</summary>
        public int ExtractedCount { get; internal set; }

        /// <summary>Gets the number of existing outputs skipped.</summary>
        public int SkippedCount { get; internal set; }

        /// <summary>Gets whether a progress callback canceled the batch.</summary>
        public bool Canceled { get; internal set; }

        /// <summary>Gets errors encountered during extraction.</summary>
        public IReadOnlyList<string> Errors => _errors;

        private readonly List<string> _errors = new();

        internal void AddError(string error)
        {
            _errors.Add(error);
        }
    }

    /// <summary>
    /// Extracts sprites from explicit asset paths and pixel rectangles without an editor window.
    /// </summary>
    public static class SpriteSheetExtractionAPI
    {
        /// <summary>
        /// Extracts selected sprites and returns counts and errors without displaying prompts.
        /// </summary>
        public static SpriteSheetExtractionResult Extract(
            IReadOnlyList<SpriteSheetExtractionRequest> requests,
            bool overwriteExisting = false,
            bool preserveImportSettings = true,
            bool dryRun = false,
            Func<int, int, bool> cancelRequested = null
        )
        {
            SpriteSheetExtractionResult result = new();
            if (requests == null)
            {
                result.AddError("Requests are null.");
                return result;
            }

            Dictionary<string, bool> readability = new(StringComparer.OrdinalIgnoreCase);
            List<SpriteSheetExtractionRequest> written = new();
            try
            {
                for (int i = 0; i < requests.Count; ++i)
                {
                    if (cancelRequested != null && cancelRequested(i, requests.Count))
                    {
                        result.Canceled = true;
                        break;
                    }

                    SpriteSheetExtractionRequest request = requests[i];
                    if (!Validate(request, result))
                    {
                        continue;
                    }

                    if (!overwriteExisting && File.Exists(ToFullPath(request.OutputAssetPath)))
                    {
                        ++result.SkippedCount;
                        continue;
                    }

                    if (dryRun)
                    {
                        ++result.ExtractedCount;
                        continue;
                    }

                    if (!TryWrite(request, readability, result))
                    {
                        continue;
                    }

                    written.Add(request);
                    ++result.ExtractedCount;
                }

                if (!dryRun && written.Count > 0)
                {
                    using (AssetDatabaseBatchHelper.BeginBatch())
                    {
                        foreach (SpriteSheetExtractionRequest request in written)
                        {
                            try
                            {
                                AssetDatabase.ImportAsset(request.OutputAssetPath);
                            }
                            catch (Exception error)
                            {
                                result.AddError(
                                    $"Failed to import '{request.OutputAssetPath}': {error.Message}"
                                );
                            }
                        }
                    }

                    if (preserveImportSettings)
                    {
                        using (AssetDatabaseBatchHelper.BeginBatch())
                        {
                            foreach (SpriteSheetExtractionRequest request in written)
                            {
                                try
                                {
                                    if (
                                        readability.TryGetValue(
                                            request.SourceAssetPath,
                                            out bool sourceWasReadable
                                        )
                                    )
                                    {
                                        ApplyImportSettings(request, sourceWasReadable, result);
                                    }
                                    else
                                    {
                                        result.AddError(
                                            $"Missing source state for '{request.SourceAssetPath}'."
                                        );
                                    }
                                }
                                catch (Exception error)
                                {
                                    result.AddError(
                                        $"Failed to apply settings to '{request.OutputAssetPath}': {error.Message}"
                                    );
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception error)
            {
                result.AddError($"Extraction failed: {error.Message}");
            }
            finally
            {
                foreach (KeyValuePair<string, bool> pair in readability)
                {
                    if (pair.Value)
                    {
                        continue;
                    }

                    try
                    {
                        if (AssetImporter.GetAtPath(pair.Key) is TextureImporter importer)
                        {
                            importer.isReadable = false;
                            importer.SaveAndReimport();
                        }
                    }
                    catch (Exception error)
                    {
                        result.AddError(
                            $"Failed to restore '{pair.Key}' readability: {error.Message}"
                        );
                    }
                }

                if (!dryRun && written.Count > 0)
                {
                    try
                    {
                        AssetDatabase.SaveAssets();
                    }
                    catch (Exception error)
                    {
                        result.AddError($"Failed to save extracted assets: {error.Message}");
                    }
                }
            }

            return result;
        }

        private static bool Validate(
            SpriteSheetExtractionRequest request,
            SpriteSheetExtractionResult result
        )
        {
            if (
                request == null
                || string.IsNullOrWhiteSpace(request.SourceAssetPath)
                || string.IsNullOrWhiteSpace(request.OutputAssetPath)
                || !IsSafeAssetPath(request.SourceAssetPath)
                || !IsSafeAssetPath(request.OutputAssetPath)
                || string.Equals(
                    request.SourceAssetPath,
                    request.OutputAssetPath,
                    StringComparison.OrdinalIgnoreCase
                )
                || !request.OutputAssetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                || !AssetDatabase.IsValidFolder(
                    Path.GetDirectoryName(request.OutputAssetPath)?.Replace('\\', '/')
                )
            )
            {
                result.AddError("Extraction request has invalid asset paths.");
                return false;
            }

            Rect rect = request.SourceRect;
            if (
                !float.IsFinite(rect.x)
                || !float.IsFinite(rect.y)
                || !float.IsFinite(rect.width)
                || !float.IsFinite(rect.height)
                || rect.width <= 0f
                || rect.height <= 0f
            )
            {
                result.AddError($"Invalid sprite rectangle for '{request.SourceAssetPath}'.");
                return false;
            }

            if (AssetImporter.GetAtPath(request.SourceAssetPath) is not TextureImporter)
            {
                result.AddError($"Source is not a texture: '{request.SourceAssetPath}'.");
                return false;
            }

            return true;
        }

        private static bool IsSafeAssetPath(string assetPath)
        {
            bool underAssets = assetPath.StartsWith("Assets/", StringComparison.Ordinal);
            bool underPackages = assetPath.StartsWith("Packages/", StringComparison.Ordinal);
            if ((!underAssets && !underPackages) || assetPath.IndexOf('\\') >= 0)
            {
                return false;
            }

            try
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string assetRoot = Path.GetFullPath(
                    Path.Combine(projectRoot, underAssets ? "Assets" : "Packages")
                );
                string candidate = Path.GetFullPath(ToFullPath(assetPath));
                return candidate.StartsWith(
                    assetRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase
                );
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string ToFullPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, assetPath);
        }

        private static bool TryWrite(
            SpriteSheetExtractionRequest request,
            Dictionary<string, bool> readability,
            SpriteSheetExtractionResult result
        )
        {
            try
            {
                TextureImporter importer = (TextureImporter)
                    AssetImporter.GetAtPath(request.SourceAssetPath);
                if (!readability.ContainsKey(request.SourceAssetPath))
                {
                    readability.Add(request.SourceAssetPath, importer.isReadable);
                    if (!importer.isReadable)
                    {
                        importer.isReadable = true;
                        importer.SaveAndReimport();
                    }
                }

                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    request.SourceAssetPath
                );
                if (texture == null)
                {
                    result.AddError($"Failed to load '{request.SourceAssetPath}'.");
                    return false;
                }

                int x = Mathf.FloorToInt(request.SourceRect.x);
                int y = Mathf.FloorToInt(request.SourceRect.y);
                int width = Mathf.FloorToInt(request.SourceRect.width);
                int height = Mathf.FloorToInt(request.SourceRect.height);
                if (texture.width <= 0 || texture.height <= 0)
                {
                    result.AddError($"Source texture has no pixels: '{request.SourceAssetPath}'.");
                    return false;
                }

                x = Mathf.Clamp(x, 0, texture.width - 1);
                y = Mathf.Clamp(y, 0, texture.height - 1);
                width = Mathf.Clamp(width, 1, texture.width - x);
                height = Mathf.Clamp(height, 1, texture.height - y);

                Color32[] source = texture.GetPixels32();
                Texture2D extracted = new(width, height, TextureFormat.RGBA32, false);
                try
                {
                    using PooledArray<Color32> pixelsLease = SystemArrayPool<Color32>.Get(
                        checked(width * height),
                        out Color32[] pixels
                    );
                    SpriteSheetExtractor.CopyPixelRows(
                        source,
                        texture.width,
                        x,
                        y,
                        width,
                        height,
                        pixels
                    );
                    SpriteSheetExtractor.ApplyPixelBuffer(extracted, width, height, pixels);
                    File.WriteAllBytes(
                        ToFullPath(request.OutputAssetPath),
                        extracted.EncodeToPNG()
                    );
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(extracted);
                }

                return true;
            }
            catch (Exception error)
            {
                result.AddError($"Failed to extract '{request.SourceAssetPath}': {error.Message}");
                return false;
            }
        }

        private static void ApplyImportSettings(
            SpriteSheetExtractionRequest request,
            bool sourceWasReadable,
            SpriteSheetExtractionResult result
        )
        {
            if (
                AssetImporter.GetAtPath(request.SourceAssetPath) is not TextureImporter source
                || AssetImporter.GetAtPath(request.OutputAssetPath)
                    is not TextureImporter destination
            )
            {
                result.AddError($"Failed to load importers for '{request.OutputAssetPath}'.");
                return;
            }

            destination.textureType = TextureImporterType.Sprite;
            destination.spriteImportMode = SpriteImportMode.Single;
            destination.spritePixelsPerUnit = source.spritePixelsPerUnit;
            destination.filterMode = source.filterMode;
            destination.textureCompression = source.textureCompression;
            destination.wrapMode = source.wrapMode;
            destination.mipmapEnabled = source.mipmapEnabled;
            destination.isReadable = sourceWasReadable;

            TextureImporterSettings settings = new();
            destination.ReadTextureSettings(settings);
            settings.spritePivot = request.Pivot;
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spriteBorder = request.Border;
            destination.SetTextureSettings(settings);
            destination.spritePivot = request.Pivot;
            try
            {
                TextureImporterPlatformSettings platform =
                    source.GetDefaultPlatformTextureSettings();
                if (!string.IsNullOrWhiteSpace(platform.name))
                {
                    destination.SetPlatformTextureSettings(platform);
                }
            }
            catch (Exception error)
            {
                result.AddError(
                    $"Failed to copy platform settings for '{request.OutputAssetPath}': {error.Message}"
                );
            }

            destination.SaveAndReimport();
        }
    }
#endif
}
