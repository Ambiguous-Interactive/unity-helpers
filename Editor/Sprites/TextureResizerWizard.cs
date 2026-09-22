// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.Serialization;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    /// <summary>
    /// ScriptableWizard to batch resize textures by a computed delta using a chosen algorithm.
    /// Useful for adjusting imported assets to target pixel density or scale without external tools.
    /// </summary>
    /// <remarks>
    /// <para>
    /// How it works: for each selected texture (or those discovered under provided directories), the
    /// tool computes a size increment from <c>pixelsPerUnit</c> and the width/height multipliers.
    /// A dry run reports the target size without reading pixel data or changing importer settings.
    /// A real run ensures readability, resizes via bilinear or point, and writes the PNG to the
    /// original path or a selected output folder.
    /// </para>
    /// <para>
    /// Pros: fast iteration inside Unity, supports multiple discovery paths, preserves import
    /// settings, and can be run multiple times (<c>numResizes</c>) for step changes.
    /// </para>
    /// <para>
    /// Caveats: real runs replace original files when no output folder is selected. If textures
    /// are non-readable, a real run temporarily changes their importer settings.
    /// </para>
    /// <example>
    /// <![CDATA[
    /// // Open from menu: Tools/Wallstop Studios/Unity Helpers/Texture Resizer
    /// // Typical settings for retro pixel art:
    /// //   scalingResizeAlgorithm = Point
    /// //   pixelsPerUnit = 100
    /// //   widthMultiplier = 1.0f, heightMultiplier = 1.0f (double by setting numResizes=1 and multipliers)
    /// ]]>
    /// </example>
    public sealed class TextureResizerWizard : ScriptableWizard
    {
        public List<Texture2D> textures = new();

        [FormerlySerializedAs("animationSources")]
        [Tooltip(
            "Drag a folder from Unity here to apply the configuration to all textures under it. No textures are modified if no directories are provided."
        )]
        public List<Object> textureSourcePaths = new();

        public int numResizes = 1;

        [Tooltip("Resize algorithm to use for scaling.")]
        public ResizeAlgorithm scalingResizeAlgorithm = ResizeAlgorithm.Bilinear;

        public int pixelsPerUnit = 100;
        public float widthMultiplier = 0.54f;
        public float heightMultiplier = 0.245f;

        [Tooltip("If true, only simulates the operation without writing files.")]
        public bool dryRun;

        [Tooltip(
            "Optional output folder (Unity project relative). If set, resized PNGs are written here instead of overwriting originals."
        )]
        public DefaultAsset outputFolder;

        [MenuItem("Tools/Wallstop Studios/Unity Helpers/Texture Resizer")]
        public static void ResizeTextures()
        {
            _ = DisplayWizard<TextureResizerWizard>("Texture Resizer", "Resize");
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
                double extraWidth = Math.Round(candidateWidth / (pixelsPerUnit * widthMultiplier));
                double extraHeight = Math.Round(
                    candidateHeight / (pixelsPerUnit * heightMultiplier)
                );

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

                candidateWidth += (int)extraWidth;
                candidateHeight += (int)extraHeight;
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

        private static void EnsureDirectory(string assetPath)
        {
            // Adopt existing filesystem folders through AssetDatabase to avoid numbered duplicate directories.
            if (AssetDatabaseBatchHelper.EnsureAssetParentFolder(assetPath))
            {
                return;
            }

            // Outside Assets, ensure the physical output directory exists even when registration is unavailable.
            string dirAsset = Path.GetDirectoryName(assetPath)?.SanitizePath();
            if (string.IsNullOrEmpty(dirAsset))
            {
                return;
            }
            string fullDir = ToFullPath(dirAsset);
            if (!Directory.Exists(fullDir))
            {
                _ = Directory.CreateDirectory(fullDir);
            }
        }

        internal void OnWizardCreate()
        {
            textures ??= new List<Texture2D>();
            textureSourcePaths ??= new List<Object>();

            using PooledResource<HashSet<string>> sourcePathsResource = Buffers<string>.HashSet.Get(
                out HashSet<string> sourcePaths
            );
            {
                foreach (Object pathObj in textureSourcePaths)
                {
                    string p = AssetDatabase.GetAssetPath(pathObj);
                    if (!string.IsNullOrEmpty(p))
                    {
                        _ = sourcePaths.Add(p);
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

            ordered.Sort(static (a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
            textures.Clear();
            textures.AddRange(ordered);

            if (textures.Count <= 0 || numResizes <= 0)
            {
                return;
            }

            int processed = 0;
            int resized = 0;
            int skippedWrongExt = 0;
            int skippedZeroDelta = 0;
            int errors = 0;
            bool anyChanges = false;

            string outputDirAssetPath =
                outputFolder != null ? AssetDatabase.GetAssetPath(outputFolder) : null;

            if (
                scalingResizeAlgorithm != ResizeAlgorithm.Bilinear
                && scalingResizeAlgorithm != ResizeAlgorithm.Point
            )
            {
                this.LogError($"The resize algorithm is invalid: {scalingResizeAlgorithm}.");
                return;
            }

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
                    this.LogError($"Resize settings produce an invalid texture size.");
                    return;
                }

                if (string.IsNullOrEmpty(outputDirAssetPath))
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
                    this.LogError(
                        $"Multiple textures would write to the same output: {destinationPath}."
                    );
                    return;
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
                                this.LogError(
                                    $"Resize settings produce an invalid size for {texture.name}."
                                );
                                continue;
                            }

                            targetW = Mathf.Clamp(targetW, 1, 16384);
                            targetH = Mathf.Clamp(targetH, 1, 16384);

                            if (targetW == origW && targetH == origH)
                            {
                                ++skippedZeroDelta;
                                continue;
                            }

                            if (dryRun)
                            {
                                this.Log(
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
                                    tImporter.isReadable = true;
                                    tImporter.SaveAndReimport();

                                    working = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                                }
                            }

                            // If writing to separate folder, avoid mutating the original asset in memory.
                            Texture2D resizeSource = working;
                            Texture2D scratch = null;
                            bool useScratch = !string.IsNullOrEmpty(outputDirAssetPath);
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
                                        throw new InvalidEnumArgumentException(
                                            nameof(scalingResizeAlgorithm),
                                            (int)scalingResizeAlgorithm,
                                            typeof(ResizeAlgorithm)
                                        );
                                }

                                byte[] bytes = resizeSource.EncodeToPNG();
                                if (bytes == null || bytes.Length == 0)
                                {
                                    ++errors;
                                    this.LogError(
                                        $"Failed to encode resized texture {texture.name}."
                                    );
                                    continue;
                                }

                                string finalAssetPath = assetPath;
                                if (!string.IsNullOrEmpty(outputDirAssetPath))
                                {
                                    string fileName = Path.GetFileName(assetPath);
                                    finalAssetPath = Path.Combine(outputDirAssetPath, fileName)
                                        .SanitizePath();
                                    EnsureDirectory(finalAssetPath);
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
                                    this.LogError($"Failed to resize {texture.name}.", writeError);
                                    continue;
                                }

                                anyChanges = true;
                                ++resized;
                                this.Log(
                                    $"Resized {texture.name} from [{origW}x{origH}] to [{targetW}x{targetH}]"
                                );
                            }
                            finally
                            {
                                if (scratch != null)
                                {
                                    DestroyImmediate(scratch);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            ++errors;
                            this.LogError($"Failed to resize {texture.name}.", e);
                        }
                        finally
                        {
                            if (tImporter.isReadable != originalReadable)
                            {
                                // Pause asset editing so restoring importer settings takes effect immediately.
                                using (AssetDatabaseBatchHelper.PauseBatch())
                                {
                                    try
                                    {
                                        tImporter.isReadable = originalReadable;
                                        tImporter.SaveAndReimport();
                                    }
                                    catch { }
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

            this.Log(
                $"Summary: processed={processed}, resized={(dryRun ? "planned:" : string.Empty)}{resized}, skippedExt={skippedWrongExt}, skippedNoChange={skippedZeroDelta}, errors={errors}"
            );
        }

        public enum ResizeAlgorithm
        {
            Bilinear,
            Point,
        }
    }
#endif
}
