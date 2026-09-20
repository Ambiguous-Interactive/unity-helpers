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
    /// Computes and applies sprite pivots from pixels above an alpha cutoff,
    /// with optional regex filtering, fuzzy skip of unchanged results, and a force
    /// reimport override.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Problems this solves: aligning sprites around a perceptual center (ignoring pixels at or
    /// below the cutoff) to simplify positioning and animation.
    /// </para>
    /// <para>
    /// How it works: for each single-sprite texture in the selected folders (filtered by optional
    /// regex), computes the center of pixels using <c>alpha &gt; cutoff</c> and writes the
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

        internal static bool SuppressUserPrompts { get; set; }

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

        internal static Vector2 CalculateCenterOfMassPivot(Sprite sprite, float alphaCutoff)
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
            List<string> folders = new();
            if (_directoryPaths != null)
            {
                foreach (Object maybeDirectory in _directoryPaths)
                {
                    if (maybeDirectory != null)
                    {
                        string path = AssetDatabase.GetAssetPath(maybeDirectory);
                        if (!AssetDatabase.IsValidFolder(path))
                        {
                            this.LogWarn($"Skipping invalid path: {path}");
                            continue;
                        }
                        folders.Add(path);
                    }
                }
            }

            if (
                !SpritePivotAdjusterAPI.TryFind(
                    folders,
                    _spriteNameRegex,
                    _filesToProcess,
                    out string error
                )
            )
            {
                this.LogWarn($"{error}");
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

            SpritePivotAdjusterAPI.Options options = new()
            {
                AlphaCutoff = _alphaCutoff,
                SkipUnchanged = _skipUnchanged,
                ForceReimport = _forceReimport,
            };
            SpritePivotAdjusterAPI.Result result;
            try
            {
                result = SpritePivotAdjusterAPI.Run(
                    _filesToProcess,
                    options,
                    applyChanges: !dryRun,
                    cancelRequested: (index, total) =>
                        ShowCancelableProgress(
                            "Processing sprites",
                            $"Processing {Path.GetFileNameWithoutExtension(_filesToProcess[index])}",
                            total == 0 ? 0f : index / (float)total
                        )
                );
            }
            finally
            {
                ClearProgress();
            }

            foreach (string warning in result.Warnings)
            {
                this.LogWarn($"{warning}");
            }
            foreach (string error in result.Errors)
            {
                this.LogError($"{error}");
            }

            using PooledResource<StringBuilder> summaryLease = Buffers.StringBuilder.Get(
                out StringBuilder summary
            );
            summary.AppendLine(
                result.Canceled ? "Canceled by user."
                : dryRun ? "Dry run completed."
                : "Completed."
            );
            summary.AppendLine($"Total candidates: {result.TotalCandidates}");
            summary.AppendLine($"Single sprites processed: {result.SingleSpritesProcessed}");
            summary.AppendLine(
                "Changed pivots" + (dryRun ? " (would change)" : "") + $": {result.Changed}"
            );
            summary.AppendLine($"Skipped unchanged: {result.SkippedUnchanged}");
            summary.AppendLine($"Skipped non-readable: {result.SkippedNonReadable}");
            summary.AppendLine($"Skipped not sprite: {result.SkippedNotSprite}");
            summary.AppendLine($"Skipped missing sprite: {result.SkippedMissingSprite}");
            summary.AppendLine($"Skipped multi-sprite textures: {result.SkippedMultiSprite}");
            Info(
                dryRun ? "Sprite Pivot Adjuster — Dry Run" : "Sprite Pivot Adjuster",
                summary.ToString()
            );
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
                    + SpritePivotAdjusterAPI.PivotEpsilon
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
                        "If pivot delta < "
                            + SpritePivotAdjusterAPI.PivotEpsilon
                            + ", skip reimport to save time."
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
                if (!string.IsNullOrEmpty(_regexError))
                {
                    ShowNotification(new GUIContent("Invalid regex. Fix it before searching."));
                    return;
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
