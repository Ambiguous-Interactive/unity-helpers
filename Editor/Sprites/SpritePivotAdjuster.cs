// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
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
        private const int CenterOfMassParallelPixelThreshold = 65_536;
        private const float PivotEpsilon = 1e-3f;

        internal static bool SuppressUserPrompts { get; set; }

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

        internal SerializedObject SerializedStateForTesting => _serializedObject;

        [SerializeField]
        internal List<Object> _directoryPaths = new();

        [SerializeField]
        internal float _alphaCutoff = 0.01f;

        [SerializeField]
        internal bool _skipUnchanged = true;

        [SerializeField]
        internal bool _forceReimport;

        [SerializeField]
        private string _spriteNameRegex = ".*";

        private SerializedObject _serializedObject;
        private SerializedProperty _directoryPathsProperty;
        private List<string> _filesToProcess;
        private Regex _regex;
        private string _regexError;
        private string _lastValidatedRegex;

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
            catch { }
        }

        [MenuItem("Tools/Wallstop Studios/Unity Helpers/Sprite Pivot Adjuster")]
        public static void ShowWindow()
        {
            GetWindow<SpritePivotAdjuster>("Sprite Pivot Adjuster");
        }

        internal static bool ShouldCalculateCenterOfMassInParallel(int width, int height)
        {
            return 1 < height && CenterOfMassParallelPixelThreshold <= (long)width * height;
        }

        internal static Vector2 CalculateCenterOfMass(
            Color32[] pixels,
            int width,
            int height,
            byte alphaThreshold,
            bool parallel
        )
        {
            if (!HasExpectedPixelCount(pixels, width, height))
            {
                return new Vector2(0.5f, 0.5f);
            }

            CenterOfMassAccumulator totals;
            if (parallel)
            {
                Color32CenterOfMassJob job = new(pixels, width, alphaThreshold);
                job.RunParallel(height);
                totals = job.Totals;
            }
            else
            {
                totals = default;
                for (int y = 0; y < height; ++y)
                {
                    int rowOffset = y * width;
                    for (int x = 0; x < width; ++x)
                    {
                        if (alphaThreshold < pixels[rowOffset + x].a)
                        {
                            totals.Add(x, y);
                        }
                    }
                }
            }

            return ToPivot(totals, width, height);
        }

        internal static Vector2 CalculateCenterOfMass(
            Color[] pixels,
            int width,
            int height,
            float alphaCutoff,
            bool parallel
        )
        {
            if (!HasExpectedPixelCount(pixels, width, height))
            {
                return new Vector2(0.5f, 0.5f);
            }

            CenterOfMassAccumulator totals;
            if (parallel)
            {
                ColorCenterOfMassJob job = new(pixels, width, alphaCutoff);
                job.RunParallel(height);
                totals = job.Totals;
            }
            else
            {
                totals = default;
                for (int y = 0; y < height; ++y)
                {
                    int rowOffset = y * width;
                    for (int x = 0; x < width; ++x)
                    {
                        if (alphaCutoff < pixels[rowOffset + x].a)
                        {
                            totals.Add(x, y);
                        }
                    }
                }
            }

            return ToPivot(totals, width, height);
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

            bool parallel = ShouldCalculateCenterOfMassInParallel(width, height);
            if (startX == 0 && startY == 0 && width == texture.width && height == texture.height)
            {
                Color32[] pixels32 = texture.GetPixels32();
                byte alphaThreshold = ColorQuantization.ToThresholdByte(alphaCutoff);
                return CalculateCenterOfMass(pixels32, width, height, alphaThreshold, parallel);
            }

            Color[] pixels = texture.GetPixels(startX, startY, width, height);
            return CalculateCenterOfMass(pixels, width, height, alphaCutoff, parallel);
        }

        private static bool HasExpectedPixelCount<T>(T[] pixels, int width, int height)
        {
            return pixels != null
                && 0 < width
                && 0 < height
                && (long)width * height == pixels.LongLength;
        }

        private static Vector2 ToPivot(CenterOfMassAccumulator totals, int width, int height)
        {
            if (totals.Count == 0L)
            {
                return new Vector2(0.5f, 0.5f);
            }

            double averageX = (double)totals.SumX / totals.Count;
            double averageY = (double)totals.SumY / totals.Count;

            double pivotX = averageX / width;
            double pivotY = averageY / height;

            return new Vector2(Mathf.Clamp01((float)pivotX), Mathf.Clamp01((float)pivotY));
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

            using PooledResource<HashSet<string>> seenRes = SetBuffers<string>
                .GetHashSetPool(StringComparer.OrdinalIgnoreCase)
                .Get(out HashSet<string> seen);
            foreach (Object maybeDirectory in _directoryPaths)
            {
                if (maybeDirectory == null)
                {
                    continue;
                }

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

                    if (!SpriteFileExtensions.HasAny(file, ImageFileExtensions))
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
                        break;
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
                            continue;
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

        private void BindSerializedState()
        {
            ReleaseSerializedState();
            _serializedObject = new SerializedObject(this);
            _directoryPathsProperty = _serializedObject.FindProperty(nameof(_directoryPaths));
        }

        private void ReleaseSerializedState()
        {
            _directoryPathsProperty = null;
            _serializedObject?.Dispose();
            _serializedObject = null;
        }

        private void OnDisable()
        {
            ReleaseSerializedState();
        }

        private void OnEnable()
        {
            BindSerializedState();
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
            if (_serializedObject == null)
            {
                BindSerializedState();
            }

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

        private struct CenterOfMassAccumulator
        {
            public long SumX;
            public long SumY;
            public long Count;

            public void Add(int x, int y)
            {
                SumX += x;
                SumY += y;
                Count++;
            }
        }

        private sealed class Color32CenterOfMassJob
        {
            public CenterOfMassAccumulator Totals;

            private readonly Color32[] _pixels;
            private readonly int _width;
            private readonly byte _alphaThreshold;

            public Color32CenterOfMassJob(Color32[] pixels, int width, byte alphaThreshold)
            {
                _pixels = pixels;
                _width = width;
                _alphaThreshold = alphaThreshold;
            }

            public void RunParallel(int height)
            {
                Parallel.For(0, height, CreateAccumulator, ScanRow, MergeAccumulator);
            }

            private CenterOfMassAccumulator CreateAccumulator()
            {
                return default;
            }

            private CenterOfMassAccumulator ScanRow(
                int y,
                ParallelLoopState _,
                CenterOfMassAccumulator local
            )
            {
                int rowOffset = y * _width;
                for (int x = 0; x < _width; ++x)
                {
                    if (_alphaThreshold < _pixels[rowOffset + x].a)
                    {
                        local.Add(x, y);
                    }
                }

                return local;
            }

            private void MergeAccumulator(CenterOfMassAccumulator local)
            {
                Interlocked.Add(ref Totals.SumX, local.SumX);
                Interlocked.Add(ref Totals.SumY, local.SumY);
                Interlocked.Add(ref Totals.Count, local.Count);
            }
        }

        private sealed class ColorCenterOfMassJob
        {
            public CenterOfMassAccumulator Totals;

            private readonly Color[] _pixels;
            private readonly int _width;
            private readonly float _alphaCutoff;

            public ColorCenterOfMassJob(Color[] pixels, int width, float alphaCutoff)
            {
                _pixels = pixels;
                _width = width;
                _alphaCutoff = alphaCutoff;
            }

            public void RunParallel(int height)
            {
                Parallel.For(0, height, CreateAccumulator, ScanRow, MergeAccumulator);
            }

            private CenterOfMassAccumulator CreateAccumulator()
            {
                return default;
            }

            private CenterOfMassAccumulator ScanRow(
                int y,
                ParallelLoopState _,
                CenterOfMassAccumulator local
            )
            {
                int rowOffset = y * _width;
                for (int x = 0; x < _width; ++x)
                {
                    if (_alphaCutoff < _pixels[rowOffset + x].a)
                    {
                        local.Add(x, y);
                    }
                }

                return local;
            }

            private void MergeAccumulator(CenterOfMassAccumulator local)
            {
                Interlocked.Add(ref Totals.SumX, local.SumX);
                Interlocked.Add(ref Totals.SumY, local.SumY);
                Interlocked.Add(ref Totals.Count, local.Count);
            }
        }
    }
#endif
}
