// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO;
    using System.Linq;
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
    /// tool ensures readability, clones the texture, computes a size increment from
    /// <c>pixelsPerUnit</c> and the width/height multipliers, resizes via bilinear or point, and
    /// writes the PNG back to the original asset path. It refreshes the AssetDatabase between
    /// passes for multi-iteration resizing.
    /// </para>
    /// <para>
    /// Pros: fast iteration inside Unity, supports multiple discovery paths, preserves import
    /// settings, and can be run multiple times (<c>numResizes</c>) for step changes.
    /// </para>
    /// <para>
    /// Caveats: overwrites files in-place; ensure version control. If textures are non-readable,
    /// importer is temporarily toggled which may dirties the asset. Consider backing up.
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
        public enum ResizeAlgorithm
        {
            Bilinear,
            Point,
        }

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

        internal void OnWizardCreate()
        {
            textures ??= new List<Texture2D>();
            textureSourcePaths ??= new List<Object>();

            // Discover textures from provided folders using a pooled set to avoid duplicates.
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

                if (sourcePaths.Count > 0)
                {
                    foreach (
                        string guid in AssetDatabase.FindAssets(
                            "t:texture2D",
                            sourcePaths.ToArray()
                        )
                    )
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

            // Remove nulls, de-dupe, and order by name without LINQ
            using PooledResource<HashSet<Texture2D>> distinctResource =
                Buffers<Texture2D>.HashSet.Get(out HashSet<Texture2D> distinct);
            using PooledResource<List<Texture2D>> orderedResource = Buffers<Texture2D>.List.Get(
                out List<Texture2D> ordered
            );
            for (int i = 0; i < textures.Count; i++)
            {
                Texture2D t = textures[i];
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

            // Batch edits for performance
            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
            {
                for (int idx = 0; idx < textures.Count; ++idx)
                {
                    Texture2D texture = textures[idx];
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

                    // Progress/cancel UI
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
                        if (!originalReadable)
                        {
                            // Temporarily exit batch scope to guarantee reimport applies immediately.
                            // We need to pause the batch since SaveAndReimport requires the AssetDatabase
                            // to not be in editing mode to process the import.
                            // Use a nested batch scope to properly track the exit and re-entry.
                            using (AssetDatabaseBatchHelper.PauseBatch())
                            {
                                tImporter.isReadable = true;
                                tImporter.SaveAndReimport();
                                // Reload to ensure readability state reflects on the instance
                                working = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                            }
                        }

                        int origW = working.width;
                        int origH = working.height;

                        // Compute final target size by simulating numResizes passes.
                        (int targetW, int targetH) = ComputeFinalSize(
                            origW,
                            origH,
                            numResizes,
                            pixelsPerUnit,
                            widthMultiplier,
                            heightMultiplier
                        );

                        // Clamp to sane limits
                        targetW = Mathf.Clamp(targetW, 1, 16384);
                        targetH = Mathf.Clamp(targetH, 1, 16384);

                        if (targetW == working.width && targetH == working.height)
                        {
                            ++skippedZeroDelta;
                            continue;
                        }

                        // If writing to separate folder, avoid mutating the original asset in memory.
                        Texture2D resizeSource = working;
                        Texture2D scratch = null;
                        bool useScratch = !string.IsNullOrEmpty(outputDirAssetPath);
                        if (useScratch)
                        {
                            // Create a scratch texture and copy pixels; avoids modifying the original in memory.
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

                            if (dryRun)
                            {
                                this.Log(
                                    $"[DryRun] Would resize {texture.name} to [{targetW}x{targetH}]"
                                );
                                ++resized; // counts as a planned resize
                                continue;
                            }

                            // Encode and atomically write the PNG
                            byte[] bytes = resizeSource.EncodeToPNG();

                            string finalAssetPath = assetPath;
                            if (!string.IsNullOrEmpty(outputDirAssetPath))
                            {
                                string fileName = Path.GetFileName(assetPath);
                                finalAssetPath = Path.Combine(outputDirAssetPath, fileName)
                                    .SanitizePath();
                                EnsureDirectory(finalAssetPath);
                            }

                            string fullDest = ToFullPath(finalAssetPath);
                            string tempPath = fullDest + ".tmp";

                            File.WriteAllBytes(tempPath, bytes);

                            if (File.Exists(fullDest))
                            {
                                string backupPath = fullDest + ".bak";
                                File.Replace(tempPath, fullDest, backupPath, true);
                                // Best-effort cleanup of backup to avoid clutter in VCS; keep if replace failed.
                                try
                                {
                                    File.Delete(backupPath);
                                }
                                catch
                                { /* ignore */
                                }
                            }
                            else
                            {
                                // New file write
                                File.Move(tempPath, fullDest);
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
                        // Restore importer readability to original state
                        if (tImporter.isReadable != originalReadable)
                        {
                            // Exit batch temporarily to restore importer reliably.
                            // Use PauseBatch to properly track the exit and re-entry.
                            using (AssetDatabaseBatchHelper.PauseBatch())
                            {
                                try
                                {
                                    tImporter.isReadable = originalReadable;
                                    tImporter.SaveAndReimport();
                                }
                                catch
                                { /* ignore restore errors */
                                }
                            }
                        }
                    }
                }
            }

            Utils.EditorUi.ClearProgress();

            if (anyChanges)
            {
                AssetDatabase.Refresh();
            }

            this.Log(
                $"Summary: processed={processed}, resized={(dryRun ? "planned:" : string.Empty)}{resized}, skippedExt={skippedWrongExt}, skippedNoChange={skippedZeroDelta}, errors={errors}"
            );
        }

        private static (int width, int height) ComputeFinalSize(
            int startWidth,
            int startHeight,
            int passes,
            int pixelsPerUnit,
            float widthMultiplier,
            float heightMultiplier
        )
        {
            int w = startWidth;
            int h = startHeight;
            for (int i = 0; i < passes; ++i)
            {
                int extraWidth = (int)Math.Round(w / (pixelsPerUnit * widthMultiplier));
                int extraHeight = (int)Math.Round(h / (pixelsPerUnit * heightMultiplier));
                // If both are zero, further passes won’t change the size; break early.
                if (extraWidth == 0 && extraHeight == 0)
                {
                    break;
                }

                w += extraWidth;
                h += extraHeight;
            }

            return (w, h);
        }

        private static string ToFullPath(string assetPath)
        {
            // Convert "Assets/..." to full system path
            string projectRoot = Application.dataPath.Substring(
                0,
                Application.dataPath.Length - "Assets".Length
            );
            return Path.Combine(projectRoot, assetPath).SanitizePath();
        }

        private static void EnsureDirectory(string assetPath)
        {
            // Route through the single hardened folder helper. Creating the directory on disk
            // first and then calling AssetDatabase.CreateFolder per-segment (the previous
            // approach) fabricates numbered-duplicate folders ("Name 1") because each segment
            // already exists on disk by the time CreateFolder runs — the same on-disk/AssetDatabase
            // desync that broke the singleton folders. EnsureAssetParentFolder adopts existing
            // on-disk folders via import instead.
            if (AssetDatabaseBatchHelper.EnsureAssetParentFolder(assetPath))
            {
                return;
            }

            // Fallback: the helper refuses paths outside the Assets tree (and returns false if a
            // segment cannot be registered). Preserve the prior guarantee that the physical output
            // directory exists so the subsequent asset write cannot fail with "directory not found".
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
    }
#endif
}
