// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using CustomEditors;
    using UnityEditor;
    using UnityEngine;
    using Utils;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Computes and applies a new sprite pivot based on an alpha-weighted center-of-mass
    /// calculation, with optional regex filtering, fuzzy skip of unchanged results, and a force
    /// reimport override.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Problems this solves: aligning sprites around a perceptual center (ignoring transparent
    /// pixels below a cutoff) to simplify positioning and animation.
    /// </para>
    /// <para>
    /// How it works: for each single-sprite texture in the selected folders (filtered by optional
    /// regex), computes the pixel-weighted centroid using <c>alpha &gt;= cutoff</c> and writes the
    /// pivot into the importer settings.
    /// </para>
    /// <para>
    /// Pros: predictable pivots for varied silhouettes; skip unchanged to speed runs.
    /// Caveats: multi-sprite textures are not supported; importer may be dirtied frequently.
    /// </para>
    /// </remarks>
    public class SpritePivotAdjuster : EditorWindow
    {
        internal static bool SuppressUserPrompts { get; set; }
        private const float PivotEpsilon = 1e-3f;

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

        [SerializeField]
        internal List<Object> _directoryPaths = new();

        [SerializeField]
        private string _spriteNameRegex = ".*";

        [SerializeField]
        internal float _alphaCutoff = 0.01f;

        [SerializeField]
        internal bool _skipUnchanged = true;

        [SerializeField]
        internal bool _forceReimport;

        private SerializedObject _serializedObject;
        private SerializedProperty _directoryPathsProperty;
        private List<string> _filesToProcess;
        private Regex _regex;
        private string _regexError;
        private string _lastValidatedRegex;

        [MenuItem("Tools/Wallstop Studios/Unity Helpers/Sprite Pivot Adjuster")]
        public static void ShowWindow()
        {
            GetWindow<SpritePivotAdjuster>("Sprite Pivot Adjuster");
        }

        static SpritePivotAdjuster()
        {
            // Auto-suppress UI prompts in batch mode and test runs
            try
            {
                if (Application.isBatchMode || Utils.EditorUi.Suppress)
                {
                    SuppressUserPrompts = true;
                }
            }
            catch
            {
                // Ignore environment probing failures
            }
        }

        private static bool IsInvokedByTestRunner()
        {
            // Heuristic: Unity test runs pass -runTests/-testResults on the command line
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; ++i)
            {
                string a = args[i];
                if (
                    0 <= a.IndexOf("runTests", StringComparison.OrdinalIgnoreCase)
                    || 0 <= a.IndexOf("testResults", StringComparison.OrdinalIgnoreCase)
                    || 0 <= a.IndexOf("testPlatform", StringComparison.OrdinalIgnoreCase)
                )
                {
                    return true;
                }
            }
            return false;
        }

        private void OnEnable()
        {
            _serializedObject = new SerializedObject(this);
            _directoryPathsProperty = _serializedObject.FindProperty(nameof(_directoryPaths));
            // _spriteNameRegexProperty was unused; using direct field to enable inline validation and tooltips.
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField(
                new GUIContent(
                    "Input Directories",
                    "Folders to scan for textures. Only supported image extensions are considered."
                ),
                EditorStyles.boldLabel
            );
            _serializedObject.Update();
            PersistentDirectoryGUI.PathSelectorObjectArray(
                _directoryPathsProperty,
                nameof(SpriteCropper)
            );

            using (new GUILayout.HorizontalScope())
            {
                _spriteNameRegex = EditorGUILayout.TextField(
                    new GUIContent(
                        "Sprite Name Regex",
                        "Optional .NET regex applied to file names (no extension). Leave empty for all."
                    ),
                    _spriteNameRegex
                );
            }

            // Inline regex validation (validate only when the text changes to avoid per-frame cost)
            if (!string.Equals(_spriteNameRegex, _lastValidatedRegex, StringComparison.Ordinal))
            {
                _lastValidatedRegex = _spriteNameRegex;
                if (string.IsNullOrWhiteSpace(_spriteNameRegex))
                {
                    _regexError = null;
                }
                else
                {
                    try
                    {
                        _ = new Regex(_spriteNameRegex, RegexOptions.CultureInvariant);
                        _regexError = null;
                    }
                    catch (ArgumentException e)
                    {
                        _regexError = e.Message;
                    }
                }
            }

            if (!string.IsNullOrEmpty(_regexError))
            {
                EditorGUILayout.HelpBox($"Invalid regex: {_regexError}", MessageType.Error);
            }

            EditorGUILayout.HelpBox(
                "Single-sprite textures only. Alpha Cutoff ignores pixels at/under the threshold when computing center-of-mass pivot. 'Skip Unchanged' avoids reimport if change < "
                    + PivotEpsilon
                    + ". 'Force Reimport' overrides that.",
                MessageType.Info
            );

            _alphaCutoff = EditorGUILayout.Slider(
                new GUIContent(
                    "Alpha Cutoff",
                    "Pixels with alpha <= cutoff are ignored when computing the pivot."
                ),
                _alphaCutoff,
                0f,
                1f
            );

            using (new GUILayout.HorizontalScope())
            {
                _skipUnchanged = EditorGUILayout.ToggleLeft(
                    new GUIContent(
                        "Skip Unchanged (fuzzy)",
                        "If pivot delta < " + PivotEpsilon + ", skip reimport to save time."
                    ),
                    _skipUnchanged
                );
            }

            using (new GUILayout.HorizontalScope())
            {
                _forceReimport = EditorGUILayout.ToggleLeft(
                    new GUIContent(
                        "Force Reimport",
                        "Reimport even if the computed pivot is unchanged. Overrides 'Skip Unchanged'."
                    ),
                    _forceReimport
                );
            }

            _serializedObject.ApplyModifiedProperties();
            if (
                GUILayout.Button(
                    new GUIContent(
                        "Find Sprites To Process",
                        "Scan selected folders and filter by regex."
                    )
                )
            )
            {
                _regex = null;
                if (!string.IsNullOrEmpty(_regexError))
                {
                    ShowNotification(new GUIContent("Invalid regex. Fix it before searching."));
                    return;
                }
                if (!string.IsNullOrWhiteSpace(_spriteNameRegex))
                {
                    try
                    {
                        _regex = new Regex(
                            _spriteNameRegex,
                            RegexOptions.Compiled | RegexOptions.CultureInvariant
                        );
                    }
                    catch (ArgumentException e)
                    {
                        this.LogWarn($"Invalid regex '{_spriteNameRegex}'", e);
                        ShowNotification(new GUIContent("Invalid regex. Fix it before searching."));
                        return;
                    }
                }
                FindFilesToProcess();
            }

            if (_filesToProcess is { Count: > 0 })
            {
                GUILayout.Label(
                    $"Found {_filesToProcess.Count} sprites to process.",
                    EditorStyles.boldLabel
                );
                using (new GUILayout.HorizontalScope())
                {
                    if (
                        GUILayout.Button(
                            new GUIContent(
                                "Dry Run",
                                "Simulate pivot changes without applying. Shows a brief summary."
                            )
                        )
                    )
                    {
                        AdjustPivotsInDirectory(dryRun: true);
                    }
                    if (
                        GUILayout.Button(
                            new GUIContent(
                                "Adjust Pivots in Directory",
                                "Compute and apply pivots (cancelable). Honors Alpha Cutoff, Skip Unchanged, and Force Reimport."
                            )
                        )
                    )
                    {
                        AdjustPivotsInDirectory(dryRun: false);
                        _filesToProcess = null;
                    }
                }
            }
            else if (_filesToProcess != null)
            {
                GUILayout.Label(
                    "No sprites found to process in the selected directories.",
                    EditorStyles.label
                );
            }
        }

        internal void FindFilesToProcess()
        {
            _filesToProcess ??= new List<string>();
            _filesToProcess.Clear();
            if (_directoryPaths is not { Count: > 0 })
            {
                this.LogWarn($"No input directories selected.");
                return;
            }

            // Use AssetDatabase to find textures in selected folders to ensure asset-relative paths.
            using PooledResource<HashSet<string>> seenRes = SetBuffers<string>
                .GetHashSetPool(StringComparer.OrdinalIgnoreCase)
                .Get(out HashSet<string> seen);
            foreach (Object maybeDirectory in _directoryPaths.Where(d => d != null))
            {
                string assetPath = AssetDatabase.GetAssetPath(maybeDirectory);
                if (!AssetDatabase.IsValidFolder(assetPath))
                {
                    this.LogWarn($"Skipping invalid path: {assetPath}");
                    continue;
                }

                string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { assetPath });
                foreach (string guid in guids)
                {
                    string file = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(file))
                    {
                        continue;
                    }

                    // Extension filter
                    if (
                        !Array.Exists(
                            ImageFileExtensions,
                            ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)
                        )
                    )
                    {
                        continue;
                    }

                    string fileName = Path.GetFileNameWithoutExtension(file);
                    if (_regex != null && !_regex.IsMatch(fileName))
                    {
                        continue;
                    }
                    if (seen.Add(file))
                    {
                        _filesToProcess.Add(file);
                    }
                }
            }
            Repaint();
        }

        internal void AdjustPivotsInDirectory(bool dryRun)
        {
            if (_filesToProcess == null || _filesToProcess.Count == 0)
            {
                ShowNotification(new GUIContent("Nothing to process. Run 'Find' first."));
                return;
            }
            using PooledResource<HashSet<string>> processedFilesRes = SetBuffers<string>
                .GetHashSetPool(StringComparer.OrdinalIgnoreCase)
                .Get(out HashSet<string> processedFiles);
            using PooledResource<List<TextureImporter>> importersRes =
                Buffers<TextureImporter>.List.Get(out List<TextureImporter> importers);
            int totalCandidates = _filesToProcess?.Count ?? 0;
            int processedSingles = 0;
            int changed = 0;
            int skippedUnchanged = 0;
            int skippedNonReadable = 0;
            int skippedNotSprite = 0;
            int skippedNullSprite = 0;
            int skippedNotSingle = 0;
            bool canceled = false;

            AssetDatabaseBatchScope? batchScope = dryRun
                ? null
                : AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false);
            try
            {
                if (_filesToProcess == null)
                {
                    return;
                }

                for (int i = 0; i < _filesToProcess.Count; i++)
                {
                    string assetPath = _filesToProcess[i];
                    if (!processedFiles.Add(assetPath))
                    {
                        continue;
                    }

                    if (
                        AssetImporter.GetAtPath(assetPath)
                        is not TextureImporter { textureType: TextureImporterType.Sprite } importer
                    )
                    {
                        // Not a sprite texture; skip to next file
                        skippedNotSprite++;
                        continue;
                    }

                    Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);

                    if (sprite == null)
                    {
                        skippedNullSprite++;
                        continue;
                    }

                    if (
                        ShowCancelableProgress(
                            "Processing sprites",
                            $"Processing {sprite.name}",
                            (float)i / _filesToProcess.Count
                        )
                    )
                    {
                        canceled = true;
                        break; // user canceled
                    }

                    if (importer.spriteImportMode == SpriteImportMode.Single)
                    {
                        processedSingles++;
                        if (!importer.isReadable)
                        {
                            skippedNonReadable++;
                            this.LogWarn($"Skipping non-readable texture: {assetPath}");
                            continue;
                        }

                        Vector2 newPivot = CalculateCenterOfMassPivot(sprite, _alphaCutoff);
                        Vector2 currentPivot = importer.spritePivot;
                        bool unchanged =
                            Mathf.Abs(currentPivot.x - newPivot.x) < PivotEpsilon
                            && Mathf.Abs(currentPivot.y - newPivot.y) < PivotEpsilon;
                        if (_skipUnchanged && !_forceReimport && unchanged)
                        {
                            skippedUnchanged++;
                            continue; // no meaningful change and not forced
                        }

                        if (!dryRun)
                        {
                            Undo.RecordObject(importer, "Adjust Sprite Pivot");
                            TextureImporterSettings settings = new();
                            importer.ReadTextureSettings(settings);
                            settings.spritePivot = newPivot;
                            settings.spriteAlignment = (int)SpriteAlignment.Custom;
                            importer.SetTextureSettings(settings);
                            importer.spritePivot = newPivot;
                            importers.Add(importer);
                        }

                        changed++;
                    }
                    else
                    {
                        skippedNotSingle++;
                    }
                }
            }
            finally
            {
                ClearProgress();
                batchScope?.Dispose();
                if (!dryRun)
                {
                    foreach (TextureImporter importer in importers)
                    {
                        importer.SaveAndReimport();
                    }
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }

                using PooledResource<StringBuilder> sbRes = Buffers.StringBuilder.Get(
                    out StringBuilder sb
                );
                sb.AppendLine(
                    canceled ? "Canceled by user."
                    : dryRun ? "Dry run completed."
                    : "Completed."
                );
                sb.AppendLine($"Total candidates: {totalCandidates}");
                sb.AppendLine($"Single sprites processed: {processedSingles}");
                sb.AppendLine(
                    "Changed pivots" + (dryRun ? " (would change)" : "") + $": {changed}"
                );
                sb.AppendLine($"Skipped unchanged: {skippedUnchanged}");
                sb.AppendLine($"Skipped non-readable: {skippedNonReadable}");
                sb.AppendLine($"Skipped not sprite: {skippedNotSprite}");
                sb.AppendLine($"Skipped missing sprite: {skippedNullSprite}");
                sb.AppendLine($"Skipped multi-sprite textures: {skippedNotSingle}");
                Info(
                    dryRun ? "Sprite Pivot Adjuster — Dry Run" : "Sprite Pivot Adjuster",
                    sb.ToString()
                );
            }
        }

        private static bool ShowCancelableProgress(string title, string info, float progress)
        {
            return Utils.EditorUi.CancelableProgress(title, info, progress);
        }

        private static void ClearProgress()
        {
            Utils.EditorUi.ClearProgress();
        }

        private static void Info(string title, string message)
        {
            Utils.EditorUi.Info(title, message);
        }

        private static Vector2 CalculateCenterOfMassPivot(Sprite sprite, float alphaCutoff)
        {
            Texture2D texture = sprite.texture;
            Rect spriteRect = sprite.rect;
            int startX = Mathf.FloorToInt(spriteRect.x);
            int startY = Mathf.FloorToInt(spriteRect.y);
            int width = Mathf.FloorToInt(spriteRect.width);
            int height = Mathf.FloorToInt(spriteRect.height);

            long totalX = 0;
            long totalY = 0;
            long pixelCount = 0;

            // Fast path: sprite covers entire texture, use GetPixels32 for lower allocation and faster access
            if (startX == 0 && startY == 0 && width == texture.width && height == texture.height)
            {
                Color32[] pixels32 = texture.GetPixels32();
                byte alphaThreshold = ColorQuantization.ToThresholdByte(alphaCutoff);

                Parallel.For(
                    0,
                    height,
                    () => (sumX: 0L, sumY: 0L, count: 0L),
                    (y, _, local) =>
                    {
                        int rowOffset = y * width;
                        for (int x = 0; x < width; ++x)
                        {
                            if (alphaThreshold < pixels32[rowOffset + x].a)
                            {
                                local.sumX += x;
                                local.sumY += y;
                                local.count++;
                            }
                        }
                        return local;
                    },
                    local =>
                    {
                        Interlocked.Add(ref totalX, local.sumX);
                        Interlocked.Add(ref totalY, local.sumY);
                        Interlocked.Add(ref pixelCount, local.count);
                    }
                );
            }
            else
            {
                Color[] pixels = texture.GetPixels(startX, startY, width, height);

                Parallel.For(
                    0,
                    height,
                    () => (sumX: 0L, sumY: 0L, count: 0L),
                    (y, _, local) =>
                    {
                        int rowOffset = y * width;
                        for (int x = 0; x < width; ++x)
                        {
                            if (alphaCutoff < pixels[rowOffset + x].a)
                            {
                                local.sumX += x;
                                local.sumY += y;
                                local.count++;
                            }
                        }
                        return local;
                    },
                    local =>
                    {
                        Interlocked.Add(ref totalX, local.sumX);
                        Interlocked.Add(ref totalY, local.sumY);
                        Interlocked.Add(ref pixelCount, local.count);
                    }
                );
            }

            if (pixelCount == 0L)
            {
                return new Vector2(0.5f, 0.5f);
            }

            double averageX = (double)totalX / pixelCount;
            double averageY = (double)totalY / pixelCount;

            double pivotX = averageX / width;
            double pivotY = averageY / height;

            return new Vector2(Mathf.Clamp01((float)pivotX), Mathf.Clamp01((float)pivotY));
        }
    }
#endif
}
