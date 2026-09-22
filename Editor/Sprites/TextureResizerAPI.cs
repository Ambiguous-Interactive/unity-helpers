// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Utils;
    using ResizeAlgorithm = TextureResizerWizard.ResizeAlgorithm;

    /// <summary>
    /// Resizes PNG texture assets from explicit editor inputs.
    /// </summary>
    public static class TextureResizerAPI
    {
        private const int MaxTextureSize = 16384;

        /// <summary>
        /// Resizes selected PNG textures and textures found under the supplied asset folders.
        /// </summary>
        /// <remarks>
        /// A dry run leaves textures, importers, and files unchanged. A real run writes PNG files,
        /// which Unity Undo cannot fully reverse. A failed or canceled run may leave earlier textures resized.
        /// </remarks>
        /// <param name="selectedTextures">Textures to resize directly.</param>
        /// <param name="sourceFolderAssetPaths">Asset folders to scan; null or empty entries are skipped.</param>
        /// <param name="numResizes">Number of resize passes.</param>
        /// <param name="scalingResizeAlgorithm">Pixel scaling algorithm.</param>
        /// <param name="pixelsPerUnit">Scale denominator in pixels.</param>
        /// <param name="widthMultiplier">Width growth multiplier.</param>
        /// <param name="heightMultiplier">Height growth multiplier.</param>
        /// <param name="outputDirAssetPath">Destination asset folder, or null to overwrite sources.</param>
        /// <param name="dryRun">Whether to preview without changing assets.</param>
        /// <returns>True when processing completes without errors or cancellation.</returns>
        public static bool TryResizeTextures(
            IReadOnlyCollection<Texture2D> selectedTextures,
            IReadOnlyCollection<string> sourceFolderAssetPaths,
            int numResizes,
            ResizeAlgorithm scalingResizeAlgorithm,
            int pixelsPerUnit,
            float widthMultiplier,
            float heightMultiplier,
            string outputDirAssetPath,
            bool dryRun
        )
        {
            try
            {
                return TryResizeTexturesCore(
                    selectedTextures,
                    sourceFolderAssetPaths,
                    numResizes,
                    scalingResizeAlgorithm,
                    pixelsPerUnit,
                    widthMultiplier,
                    heightMultiplier,
                    outputDirAssetPath,
                    dryRun
                );
            }
            catch (Exception error)
            {
                Debug.LogError($"Texture resizing failed. {error}");
                return false;
            }
        }

        private static bool TryResizeTexturesCore(
            IReadOnlyCollection<Texture2D> selectedTextures,
            IReadOnlyCollection<string> sourceFolderAssetPaths,
            int numResizes,
            ResizeAlgorithm scalingResizeAlgorithm,
            int pixelsPerUnit,
            float widthMultiplier,
            float heightMultiplier,
            string outputDirAssetPath,
            bool dryRun
        )
        {
            if (
                numResizes <= 0
                || pixelsPerUnit <= 0
                || widthMultiplier <= 0f
                || heightMultiplier <= 0f
                || float.IsNaN(widthMultiplier)
                || float.IsNaN(heightMultiplier)
                || float.IsInfinity(widthMultiplier)
                || float.IsInfinity(heightMultiplier)
            )
            {
                Debug.LogError("Resize settings produce an invalid texture size.");
                return false;
            }

            if (
                scalingResizeAlgorithm != ResizeAlgorithm.Bilinear
                && scalingResizeAlgorithm != ResizeAlgorithm.Point
            )
            {
                Debug.LogError($"The resize algorithm is invalid: {scalingResizeAlgorithm}.");
                return false;
            }

            if (outputDirAssetPath != null)
            {
                outputDirAssetPath = outputDirAssetPath.SanitizePath();
                if (
                    string.IsNullOrWhiteSpace(outputDirAssetPath)
                    || !(
                        string.Equals(outputDirAssetPath, "Assets", StringComparison.Ordinal)
                        || outputDirAssetPath.StartsWith("Assets/", StringComparison.Ordinal)
                    )
                    || !AssetDatabase.IsValidFolder(outputDirAssetPath)
                )
                {
                    Debug.LogError($"The output folder is invalid: {outputDirAssetPath}.");
                    return false;
                }
            }

            using PooledResource<List<Texture2D>> texturesResource = Buffers<Texture2D>.List.Get(
                out List<Texture2D> textures
            );
            if (selectedTextures != null)
            {
                foreach (Texture2D texture in selectedTextures)
                {
                    textures.Add(texture);
                }
            }

            using PooledResource<HashSet<string>> sourcePathsResource = Buffers<string>.HashSet.Get(
                out HashSet<string> sourcePaths
            );
            if (sourceFolderAssetPaths != null)
            {
                foreach (string path in sourceFolderAssetPaths)
                {
                    if (string.IsNullOrEmpty(path))
                    {
                        continue;
                    }

                    string normalizedPath = path.SanitizePath();
                    if (!AssetDatabase.IsValidFolder(normalizedPath))
                    {
                        Debug.LogError($"The source folder is invalid: {path}.");
                        return false;
                    }

                    _ = sourcePaths.Add(normalizedPath);
                }
            }

            if (0 < sourcePaths.Count)
            {
                string[] sourceFolders = new string[sourcePaths.Count];
                sourcePaths.CopyTo(sourceFolders);
                foreach (string guid in AssetDatabase.FindAssets("t:texture2D", sourceFolders))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path))
                    {
                        continue;
                    }

                    Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    if (texture != null)
                    {
                        textures.Add(texture);
                    }
                }
            }

            using PooledResource<HashSet<Texture2D>> distinctResource =
                Buffers<Texture2D>.HashSet.Get(out HashSet<Texture2D> distinct);
            using PooledResource<List<Texture2D>> orderedResource = Buffers<Texture2D>.List.Get(
                out List<Texture2D> ordered
            );
            foreach (Texture2D t in textures)
            {
                if (t == null)
                {
                    continue;
                }

                if (distinct.Add(t))
                {
                    ordered.Add(t);
                }
            }

            ordered.Sort(UnityObjectNameComparer<Texture2D>.Instance);
            textures.Clear();
            textures.AddRange(ordered);

            if (textures.Count <= 0)
            {
                return true;
            }

            int processed = 0;
            int resized = 0;
            int skippedWrongExt = 0;
            int skippedZeroDelta = 0;
            int errors = 0;
            bool anyChanges = false;
            bool canceled = false;

            using PooledResource<HashSet<string>> destinationPathsResource = SetBuffers<string>
                .GetHashSetPool(StringComparer.OrdinalIgnoreCase)
                .Get(out HashSet<string> destinationPaths);
            foreach (Texture2D texture in textures)
            {
                if (texture == null)
                {
                    continue;
                }

                string assetPath = AssetDatabase.GetAssetPath(texture);
                if (
                    string.IsNullOrEmpty(assetPath)
                    || !string.Equals(
                        Path.GetExtension(assetPath),
                        ".png",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    continue;
                }

                if (
                    outputDirAssetPath == null
                    && !assetPath.StartsWith("Assets/", StringComparison.Ordinal)
                )
                {
                    Debug.LogError($"Cannot overwrite a texture outside Assets: {assetPath}.");
                    return false;
                }

                if (
                    !TryComputeFinalSize(
                        texture.width,
                        texture.height,
                        numResizes,
                        pixelsPerUnit,
                        widthMultiplier,
                        heightMultiplier,
                        out _,
                        out _
                    )
                )
                {
                    Debug.LogError("Resize settings produce an invalid texture size.");
                    return false;
                }

                if (outputDirAssetPath == null)
                {
                    continue;
                }

                string destinationPath = Path.Combine(
                        outputDirAssetPath,
                        Path.GetFileName(assetPath)
                    )
                    .SanitizePath();
                if (!destinationPaths.Add(destinationPath))
                {
                    Debug.LogError(
                        $"Multiple textures would write to the same output: {destinationPath}."
                    );
                    return false;
                }
            }

            try
            {
                using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
                {
                    for (int idx = 0; idx < textures.Count; ++idx)
                    {
                        Texture2D texture = textures[idx];
                        if (texture == null)
                        {
                            continue;
                        }

                        string assetPath = AssetDatabase.GetAssetPath(texture);
                        if (string.IsNullOrEmpty(assetPath))
                        {
                            continue;
                        }

                        // Only process PNGs by default to avoid corrupting non-PNG assets.
                        if (
                            !string.Equals(
                                Path.GetExtension(assetPath),
                                ".png",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            ++skippedWrongExt;
                            continue;
                        }

                        bool cancel = Utils.EditorUi.CancelableProgress(
                            "Resizing Textures",
                            $"Processing {texture.name} ({idx + 1}/{textures.Count})",
                            (float)(idx + 1) / textures.Count
                        );
                        if (cancel)
                        {
                            canceled = true;
                            break;
                        }

                        ++processed;

                        TextureImporter tImporter =
                            AssetImporter.GetAtPath(assetPath) as TextureImporter;
                        if (tImporter == null)
                        {
                            continue;
                        }

                        bool originalReadable = tImporter.isReadable;
                        Texture2D working = texture;
                        try
                        {
                            int origW = texture.width;
                            int origH = texture.height;
                            if (
                                !TryComputeFinalSize(
                                    origW,
                                    origH,
                                    numResizes,
                                    pixelsPerUnit,
                                    widthMultiplier,
                                    heightMultiplier,
                                    out int targetW,
                                    out int targetH
                                )
                            )
                            {
                                ++errors;
                                Debug.LogError(
                                    $"Resize settings produce an invalid size for {texture.name}."
                                );
                                continue;
                            }

                            targetW = Mathf.Clamp(targetW, 1, MaxTextureSize);
                            targetH = Mathf.Clamp(targetH, 1, MaxTextureSize);

                            if (targetW == origW && targetH == origH)
                            {
                                ++skippedZeroDelta;
                                continue;
                            }

                            if (dryRun)
                            {
                                Debug.Log(
                                    $"[DryRun] Would resize {texture.name} to [{targetW}x{targetH}]"
                                );
                                ++resized;
                                continue;
                            }

                            if (!originalReadable)
                            {
                                // Pause asset editing so SaveAndReimport completes before the texture is read.
                                using (AssetDatabaseBatchHelper.PauseBatch())
                                {
                                    Undo.RecordObject(tImporter, "Resize texture importer");
                                    tImporter.isReadable = true;
                                    tImporter.SaveAndReimport();

                                    working = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                                }
                            }

                            // If writing to separate folder, avoid mutating the original asset in memory.
                            Texture2D resizeSource = working;
                            Texture2D scratch = null;
                            bool useScratch = outputDirAssetPath != null;
                            if (useScratch)
                            {
                                scratch = new Texture2D(
                                    working.width,
                                    working.height,
                                    TextureFormat.RGBA32,
                                    false
                                );
                                scratch.SetPixels(working.GetPixels());
                                scratch.Apply(false);
                                resizeSource = scratch;
                            }

                            try
                            {
                                switch (scalingResizeAlgorithm)
                                {
                                    case ResizeAlgorithm.Bilinear:
                                        TextureScale.Bilinear(resizeSource, targetW, targetH);
                                        break;
                                    case ResizeAlgorithm.Point:
                                        TextureScale.Point(resizeSource, targetW, targetH);
                                        break;
                                    default:
                                        ++errors;
                                        continue;
                                }

                                byte[] bytes = resizeSource.EncodeToPNG();
                                if (bytes == null || bytes.Length == 0)
                                {
                                    ++errors;
                                    Debug.LogError(
                                        $"Failed to encode resized texture {texture.name}."
                                    );
                                    continue;
                                }

                                string finalAssetPath = assetPath;
                                if (outputDirAssetPath != null)
                                {
                                    string fileName = Path.GetFileName(assetPath);
                                    finalAssetPath = Path.Combine(outputDirAssetPath, fileName)
                                        .SanitizePath();
                                }

                                string fullDest = ToFullPath(finalAssetPath);
                                if (
                                    !DurableFile.TryWriteAllBytes(
                                        fullDest,
                                        bytes,
                                        out Exception writeError
                                    )
                                )
                                {
                                    ++errors;
                                    Debug.LogError(
                                        $"Failed to resize {texture.name}. {writeError}"
                                    );
                                    continue;
                                }

                                anyChanges = true;
                                ++resized;
                                Debug.Log(
                                    $"Resized {texture.name} from [{origW}x{origH}] to [{targetW}x{targetH}]"
                                );
                            }
                            finally
                            {
                                if (scratch != null)
                                {
                                    UnityEngine.Object.DestroyImmediate(scratch);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            ++errors;
                            Debug.LogError($"Failed to resize {texture.name}. {e}");
                        }
                        finally
                        {
                            if (tImporter.isReadable != originalReadable)
                            {
                                // Pause asset editing so restoring importer settings takes effect immediately.
                                try
                                {
                                    using (AssetDatabaseBatchHelper.PauseBatch())
                                    {
                                        tImporter.isReadable = originalReadable;
                                        tImporter.SaveAndReimport();
                                    }
                                }
                                catch (Exception restoreError)
                                {
                                    ++errors;
                                    Debug.LogError(
                                        $"Failed to restore importer settings for {texture.name}. {restoreError}"
                                    );
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                Utils.EditorUi.ClearProgress();
            }

            if (anyChanges)
            {
                AssetDatabase.Refresh();
            }

            Debug.Log(
                $"Summary: processed={processed}, resized={(dryRun ? "planned:" : string.Empty)}{resized}, skippedExt={skippedWrongExt}, skippedNoChange={skippedZeroDelta}, errors={errors}"
            );
            return errors == 0 && !canceled;
        }

        private static bool TryComputeFinalSize(
            int startWidth,
            int startHeight,
            int passes,
            int pixelsPerUnit,
            float widthMultiplier,
            float heightMultiplier,
            out int width,
            out int height
        )
        {
            if (
                startWidth <= 0
                || startHeight <= 0
                || passes <= 0
                || pixelsPerUnit <= 0
                || widthMultiplier <= 0f
                || heightMultiplier <= 0f
                || float.IsNaN(widthMultiplier)
                || float.IsNaN(heightMultiplier)
                || float.IsInfinity(widthMultiplier)
                || float.IsInfinity(heightMultiplier)
            )
            {
                width = startWidth;
                height = startHeight;
                return false;
            }

            int candidateWidth = startWidth;
            int candidateHeight = startHeight;
            for (int i = 0; i < passes; ++i)
            {
                if (MaxTextureSize <= candidateWidth && MaxTextureSize <= candidateHeight)
                {
                    break;
                }

                double extraWidth =
                    candidateWidth < MaxTextureSize
                        ? Math.Round(candidateWidth / (pixelsPerUnit * widthMultiplier))
                        : 0d;
                double extraHeight =
                    candidateHeight < MaxTextureSize
                        ? Math.Round(candidateHeight / (pixelsPerUnit * heightMultiplier))
                        : 0d;

                if (extraWidth == 0d && extraHeight == 0d)
                {
                    break;
                }

                if (
                    int.MaxValue - candidateWidth < extraWidth
                    || int.MaxValue - candidateHeight < extraHeight
                )
                {
                    width = candidateWidth;
                    height = candidateHeight;
                    return false;
                }

                candidateWidth = (int)Math.Min(MaxTextureSize, candidateWidth + extraWidth);
                candidateHeight = (int)Math.Min(MaxTextureSize, candidateHeight + extraHeight);
            }

            width = candidateWidth;
            height = candidateHeight;
            return true;
        }

        private static string ToFullPath(string assetPath)
        {
            string projectRoot = Application.dataPath.Substring(
                0,
                Application.dataPath.Length - "Assets".Length
            );
            return Path.Combine(projectRoot, assetPath).SanitizePath();
        }
    }
#endif
}
