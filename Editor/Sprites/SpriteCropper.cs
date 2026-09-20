// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;
    using CustomEditors;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Finds and crops single-sprite textures to their minimal bounding rectangle based on alpha
    /// coverage, with optional padding and output controls. Can overwrite originals or write to a
    /// separate folder, and optionally copy default platform import settings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Problems this solves: trimming transparent margins around sprites to reduce overdraw and
    /// improve packing; standardizing sprite bounds for consistent layout.
    /// </para>
    /// <para>
    /// How it works: scans provided folders for supported image extensions and single-sprite
    /// textures, computes an alpha-threshold-based tight rect, applies optional padding, and
    /// writes the cropped PNG. Provides a "Danger Zone" utility to replace references to originals
    /// with their <c>Cropped_*</c> counterparts across assets.
    /// </para>
    /// <para>
    /// Usage:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Open via menu: Tools/Wallstop Studios/Unity Helpers/Sprite Cropper.</description></item>
    /// <item><description>Select input folders, optional name regex, and padding.</description></item>
    /// <item><description>Choose overwrite vs output directory, then Find/Process sprites.</description></item>
    /// </list>
    /// <para>
    /// Pros: reduces texture waste, quick batch processing, preserves importer options when chosen.
    /// Caveats: Multi-sprite textures are skipped; overwriting is destructive—use VCS; reference
    /// replacement is potentially dangerous and should be reviewed carefully.
    /// </para>
    /// </remarks>
    public sealed class SpriteCropper : EditorWindow
    {
        internal const float AlphaThreshold = 0.01f;

        private const string Name = "Sprite Cropper";

        private const long ParallelPixelCopyThreshold = 1_048_576L;

        private const int ParallelRowCopyThreshold = 512;

        private const long ParallelPixelScanThreshold = 8_388_608L;

        private const int ParallelScanRowThreshold = 512;

        internal SerializedObject SerializedStateForTesting => _serializedObject;

        [SerializeField]
        internal List<Object> _inputDirectories = new();

        [SerializeField]
        internal string _spriteNameRegex = ".*";

        [SerializeField]
        internal bool _onlyNecessary;

        [SerializeField]
        internal int _leftPadding;

        [SerializeField]
        internal int _rightPadding;

        [SerializeField]
        internal int _topPadding;

        [SerializeField]
        internal int _bottomPadding;

        [SerializeField]
        internal bool _overwriteOriginals;

        [SerializeField]
        internal Object _outputDirectory;

        [SerializeField]
        internal OutputReadability _outputReadability = OutputReadability.MirrorSource;

        [SerializeField]
        internal bool _copyDefaultPlatformSettings = true;

        internal List<string> _filesToProcess;
        private SerializedObject _serializedObject;
        private SerializedProperty _inputDirectoriesProperty;
        private SerializedProperty _onlyNecessaryProperty;
        private SerializedProperty _leftPaddingProperty;
        private SerializedProperty _rightPaddingProperty;
        private SerializedProperty _topPaddingProperty;
        private SerializedProperty _bottomPaddingProperty;
        private SerializedProperty _spriteNameRegexProperty;
        private SerializedProperty _overwriteOriginalsProperty;
        private SerializedProperty _outputDirectoryProperty;
        private SerializedProperty _outputReadabilityProperty;
        private SerializedProperty _copyDefaultPlatformSettingsProperty;

        private readonly List<string> _multiSpriteFiles = new();

        private bool _ackDanger;

        internal static bool ShouldCopyPixelsInParallel(int width, int height)
        {
            return ParallelPixelCopyThreshold <= (long)width * height
                && ParallelRowCopyThreshold <= height;
        }

        internal static bool ShouldScanPixelsInParallel(int width, int height)
        {
            return ParallelPixelScanThreshold <= (long)width * height
                && ParallelScanRowThreshold <= height;
        }

        internal static void CopyCropPixels(
            Color32[] pixels,
            int width,
            int height,
            int visibleMinX,
            int visibleMinY,
            int visibleMaxX,
            int visibleMaxY,
            int cropWidth,
            int cropHeight,
            Color32[] croppedPixels
        )
        {
            int srcX0 = Mathf.Max(visibleMinX, 0);
            int srcY0 = Mathf.Max(visibleMinY, 0);
            int srcX1 = Mathf.Min(visibleMaxX, width - 1);
            int srcY1 = Mathf.Min(visibleMaxY, height - 1);
            int copyStartDestX = Mathf.Max(0, srcX0 - visibleMinX);
            int copyEndDestX = Mathf.Min(cropWidth - 1, srcX1 - visibleMinX);
            int leftClear = copyStartDestX;
            int rightClear = cropWidth - 1 - copyEndDestX;
            int copyCount = copyEndDestX - copyStartDestX + 1;

            if (ShouldCopyPixelsInParallel(cropWidth, cropHeight))
            {
                CropRowCopyJob job = new(
                    pixels,
                    width,
                    height,
                    visibleMinY,
                    srcX0,
                    srcY0,
                    srcY1,
                    cropWidth,
                    leftClear,
                    rightClear,
                    copyStartDestX,
                    copyCount,
                    croppedPixels
                );
                Parallel.For(0, cropHeight, job.Execute);
                return;
            }

            for (int y = 0; y < cropHeight; ++y)
            {
                CopyCropRow(
                    y,
                    pixels,
                    width,
                    height,
                    visibleMinY,
                    srcX0,
                    srcY0,
                    srcY1,
                    cropWidth,
                    leftClear,
                    rightClear,
                    copyStartDestX,
                    copyCount,
                    croppedPixels
                );
            }
        }

        /// <summary>
        /// Computes the tight alpha-bounded crop rect (with padding) and the adjusted sprite
        /// pivot for a source image. Pure: depends only on pixels + parameters, performs NO
        /// AssetDatabase/importer I/O, so it is exercised by fast unit tests
        /// (<c>SpriteCropperMathTests</c>) instead of full texture-import round-trips.
        /// </summary>
        internal static CropComputation ComputeCrop(
            Color32[] pixels,
            int width,
            int height,
            int leftPadding,
            int rightPadding,
            int topPadding,
            int bottomPadding,
            float alphaThreshold,
            Vector2 origPivot,
            bool onlyNecessary
        )
        {
            byte alphaByteThreshold = ColorQuantization.ToThresholdByte(alphaThreshold);
            VisibleBounds bounds = ShouldScanPixelsInParallel(width, height)
                ? FindVisibleBoundsInParallel(pixels, width, height, alphaByteThreshold)
                : FindVisibleBoundsSequentially(pixels, width, height, alphaByteThreshold);
            int minX = bounds.MinX;
            int minY = bounds.MinY;
            int maxX = bounds.MaxX;
            int maxY = bounds.MaxY;
            bool hasVisible = bounds.HasVisible;

            int visibleMinX = minX;
            int visibleMinY = minY;
            int visibleMaxX = maxX;
            int visibleMaxY = maxY;

            if (hasVisible)
            {
                visibleMinX -= leftPadding;
                visibleMinY -= bottomPadding;
                visibleMaxX += rightPadding;
                visibleMaxY += topPadding;
            }
            else
            {
                visibleMinX = visibleMinY = 0;
                visibleMaxX = visibleMaxY = 0;
            }

            int cropWidth = visibleMaxX - visibleMinX + 1;
            int cropHeight = visibleMaxY - visibleMinY + 1;

            bool shouldSkip =
                onlyNecessary && (!hasVisible || (cropWidth == width && cropHeight == height));

            Vector2 origCenter = new(width * origPivot.x, height * origPivot.y);
            Vector2 newPivotPixels = origCenter - new Vector2(visibleMinX, visibleMinY);
            Vector2 newPivotNorm = new(
                0 < cropWidth ? newPivotPixels.x / cropWidth : 0.5f,
                0 < cropHeight ? newPivotPixels.y / cropHeight : 0.5f
            );

            if (!hasVisible)
            {
                newPivotNorm = new Vector2(0.5f, 0.5f);
            }

            return new CropComputation(
                hasVisible,
                shouldSkip,
                visibleMinX,
                visibleMinY,
                visibleMaxX,
                visibleMaxY,
                cropWidth,
                cropHeight,
                newPivotNorm
            );
        }

        internal static VisibleBounds FindVisibleBoundsInParallel(
            Color32[] pixels,
            int width,
            int height,
            byte alphaByteThreshold
        )
        {
            VisibleBoundsScanJob job = new(pixels, width, height, alphaByteThreshold);
            Parallel.For(0, height, job.CreateLocalBounds, job.ScanRow, job.MergeLocalBounds);
            return job.Bounds;
        }

        internal static VisibleBounds FindVisibleBoundsSequentially(
            Color32[] pixels,
            int width,
            int height,
            byte alphaByteThreshold
        )
        {
            int minX = width;
            int minY = height;
            int maxX = 0;
            int maxY = 0;
            bool hasVisible = false;
            for (int y = 0; y < height; ++y)
            {
                int rowStart = y * width;
                for (int x = 0; x < width; ++x)
                {
                    if (pixels[rowStart + x].a <= alphaByteThreshold)
                    {
                        continue;
                    }

                    hasVisible = true;
                    minX = Mathf.Min(minX, x);
                    minY = Mathf.Min(minY, y);
                    maxX = Mathf.Max(maxX, x);
                    maxY = Mathf.Max(maxY, y);
                }
            }

            return new VisibleBounds(hasVisible, minX, minY, maxX, maxY);
        }

        [MenuItem("Tools/Wallstop Studios/Unity Helpers/" + Name)]
        private static void ShowWindow() => GetWindow<SpriteCropper>(Name);

        private static void CheckPreProcessNeeded(
            string assetPath,
            Dictionary<string, bool> originalReadable
        )
        {
            string assetDirectory = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrWhiteSpace(assetDirectory))
            {
                return;
            }

            if (
                AssetImporter.GetAtPath(assetPath)
                is not TextureImporter { textureType: TextureImporterType.Sprite } importer
            )
            {
                return;
            }

            if (!importer.isReadable)
            {
                originalReadable.TryAdd(assetPath, false);
                importer.isReadable = true;
                importer.SaveAndReimport();
            }
            else
            {
                originalReadable.TryAdd(assetPath, true);
            }
        }

        private static void CopyCropRow(
            int y,
            Color32[] pixels,
            int width,
            int height,
            int visibleMinY,
            int srcX0,
            int srcY0,
            int srcY1,
            int cropWidth,
            int leftClear,
            int rightClear,
            int copyStartDestX,
            int copyCount,
            Color32[] croppedPixels
        )
        {
            int destinationRow = y * cropWidth;
            int sourceY = visibleMinY + y;
            if (sourceY < 0 || height <= sourceY || sourceY < srcY0 || srcY1 < sourceY)
            {
                Array.Clear(croppedPixels, destinationRow, cropWidth);
                return;
            }

            if (0 < leftClear)
            {
                Array.Clear(croppedPixels, destinationRow, leftClear);
            }

            if (0 < copyCount)
            {
                int sourceIndex = sourceY * width + srcX0;
                int destinationIndex = destinationRow + copyStartDestX;
                Array.Copy(pixels, sourceIndex, croppedPixels, destinationIndex, copyCount);
            }

            if (0 < rightClear)
            {
                Array.Clear(croppedPixels, destinationRow + (cropWidth - rightClear), rightClear);
            }
        }

        internal void FindFilesToProcess()
        {
            _filesToProcess ??= new List<string>();
            using PooledResource<List<string>> folderLease = Buffers<string>.List.Get(
                out List<string> folders
            );
            if (_inputDirectories != null)
            {
                foreach (Object maybeDirectory in _inputDirectories)
                {
                    if (maybeDirectory != null)
                    {
                        folders.Add(AssetDatabase.GetAssetPath(maybeDirectory));
                    }
                }
            }
            if (
                !SpriteCropperAPI.TryFind(
                    folders,
                    _spriteNameRegex,
                    _filesToProcess,
                    _multiSpriteFiles,
                    out string error
                )
            )
            {
                this.LogWarn($"{error}");
            }
            Repaint();
        }

        internal void ProcessFoundSprites()
        {
            if (_filesToProcess is not { Count: > 0 })
            {
                this.LogWarn($"No files found or selected for processing.");
                return;
            }

            string lastProcessed = null;
            bool canceled = false;
            WallstopGenericPool<HashSet<string>> processedFilesPool =
                SetBuffers<string>.GetHashSetPool(StringComparer.OrdinalIgnoreCase);
            using PooledResource<HashSet<string>> processedFilesLease = processedFilesPool.Get(
                out HashSet<string> processedFiles
            );
            using PooledResource<List<string>> needReprocessingLease = Buffers<string>.List.Get(
                out List<string> needReprocessing
            );
            WallstopGenericPool<Dictionary<string, bool>> originalReadablePool = DictionaryBuffer<
                string,
                bool
            >.GetDictionaryPool(StringComparer.OrdinalIgnoreCase);
            using PooledResource<Dictionary<string, bool>> originalReadableLease =
                originalReadablePool.Get(out Dictionary<string, bool> originalReadable);
            using PooledResource<List<TextureImporter>> newImportersLease =
                Buffers<TextureImporter>.List.Get(out List<TextureImporter> newImporters);
            {
                try
                {
                    int total = _filesToProcess.Count;
                    using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: true))
                    {
                        for (int i = 0; i < _filesToProcess.Count; ++i)
                        {
                            string file = _filesToProcess[i];
                            lastProcessed = file;
                            if (
                                Utils.EditorUi.CancelableProgress(
                                    Name,
                                    $"Pre-processing {i + 1}/{total}: {Path.GetFileName(file)}",
                                    i / (float)total
                                )
                            )
                            {
                                canceled = true;
                                break;
                            }
                            CheckPreProcessNeeded(file, originalReadable);
                        }
                        AssetDatabase.SaveAssets();
                    }

                    int totalSuccessfullyProcessed = 0;

                    if (!canceled)
                    {
                        using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: true))
                        {
                            for (int i = 0; i < _filesToProcess.Count; ++i)
                            {
                                string file = _filesToProcess[i];
                                if (!processedFiles.Add(file))
                                {
                                    continue;
                                }
                                lastProcessed = file;
                                if (
                                    Utils.EditorUi.CancelableProgress(
                                        Name,
                                        $"Processing {i + 1}/{total}: {Path.GetFileName(file)}",
                                        i / (float)total
                                    )
                                )
                                {
                                    canceled = true;
                                    break;
                                }

                                TextureImporter newImporter = ProcessSprite(
                                    file,
                                    out ProcessOutcome outcome,
                                    originalReadable
                                );
                                switch (outcome)
                                {
                                    case ProcessOutcome.Success:
                                        ++totalSuccessfullyProcessed;
                                        if (newImporter != null)
                                        {
                                            newImporters.Add(newImporter);
                                        }
                                        break;
                                    case ProcessOutcome.SkippedNoChange:

                                        break;
                                    case ProcessOutcome.RetryableError:
                                        needReprocessing.Add(file);
                                        break;
                                    case ProcessOutcome.FatalError:
                                        // Log already handled inside ProcessSprite; skip
                                        break;
                                }
                            }
                            foreach (TextureImporter newImporter in newImporters)
                            {
                                newImporter.SaveAndReimport();
                            }
                            AssetDatabase.SaveAssets();
                        }
                    }

                    if (!canceled && 0 < needReprocessing.Count)
                    {
                        newImporters.Clear();
                        using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: true))
                        {
                            foreach (string file in needReprocessing)
                            {
                                TextureImporter newImporter = ProcessSprite(
                                    file,
                                    out ProcessOutcome outcome,
                                    originalReadable
                                );
                                if (outcome == ProcessOutcome.Success && newImporter != null)
                                {
                                    ++totalSuccessfullyProcessed;
                                    newImporters.Add(newImporter);
                                }
                            }
                            foreach (TextureImporter newImporter in newImporters)
                            {
                                newImporter.SaveAndReimport();
                            }
                            AssetDatabase.SaveAssets();
                        }
                    }

                    if (0 < originalReadable.Count && !_overwriteOriginals)
                    {
                        using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: true))
                        {
                            foreach ((string path, bool wasReadable) in originalReadable)
                            {
                                try
                                {
                                    if (
                                        AssetImporter.GetAtPath(path) is TextureImporter
                                        {
                                            textureType: TextureImporterType.Sprite
                                        } srcImporter
                                    )
                                    {
                                        if (srcImporter.isReadable != wasReadable)
                                        {
                                            srcImporter.isReadable = wasReadable;
                                            srcImporter.SaveAndReimport();
                                        }
                                    }
                                }
                                catch (Exception e)
                                {
                                    this.LogError(
                                        $"Failed to restore readability for '{path}'.",
                                        e
                                    );
                                }
                            }
                            AssetDatabase.SaveAssets();
                        }
                    }

                    if (canceled)
                    {
                        this.LogWarn($"Sprite cropping canceled by user.");
                    }
                    else
                    {
                        int skipped = _filesToProcess.Count - totalSuccessfullyProcessed;
                        this.Log(
                            $"{totalSuccessfullyProcessed} sprites processed successfully. Skipped: {skipped}"
                        );
                    }
                }
                catch (Exception e)
                {
                    this.LogError(
                        $"An error occurred during processing. Last processed: {lastProcessed}.",
                        e
                    );
                }
                finally
                {
                    Utils.EditorUi.ClearProgress();
                }
            }
        }

        private void BindSerializedState()
        {
            ReleaseSerializedState();
            _serializedObject = new SerializedObject(this);
            _inputDirectoriesProperty = _serializedObject.FindProperty(nameof(_inputDirectories));
            _onlyNecessaryProperty = _serializedObject.FindProperty(nameof(_onlyNecessary));
            _leftPaddingProperty = _serializedObject.FindProperty(nameof(_leftPadding));
            _rightPaddingProperty = _serializedObject.FindProperty(nameof(_rightPadding));
            _topPaddingProperty = _serializedObject.FindProperty(nameof(_topPadding));
            _bottomPaddingProperty = _serializedObject.FindProperty(nameof(_bottomPadding));
            _spriteNameRegexProperty = _serializedObject.FindProperty(nameof(_spriteNameRegex));
            _overwriteOriginalsProperty = _serializedObject.FindProperty(
                nameof(_overwriteOriginals)
            );
            _outputDirectoryProperty = _serializedObject.FindProperty(nameof(_outputDirectory));
            _outputReadabilityProperty = _serializedObject.FindProperty(nameof(_outputReadability));
            _copyDefaultPlatformSettingsProperty = _serializedObject.FindProperty(
                nameof(_copyDefaultPlatformSettings)
            );
        }

        private void ReleaseSerializedState()
        {
            _inputDirectoriesProperty = null;
            _onlyNecessaryProperty = null;
            _leftPaddingProperty = null;
            _rightPaddingProperty = null;
            _topPaddingProperty = null;
            _bottomPaddingProperty = null;
            _spriteNameRegexProperty = null;
            _overwriteOriginalsProperty = null;
            _outputDirectoryProperty = null;
            _outputReadabilityProperty = null;
            _copyDefaultPlatformSettingsProperty = null;
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
            EditorGUILayout.LabelField("Input directories", EditorStyles.boldLabel);
            if (_serializedObject == null)
            {
                BindSerializedState();
            }

            _serializedObject.Update();
            PersistentDirectoryGUI.PathSelectorObjectArray(
                _inputDirectoriesProperty,
                nameof(SpriteCropper)
            );
            EditorGUILayout.PropertyField(_spriteNameRegexProperty, true);
            EditorGUILayout.PropertyField(_onlyNecessaryProperty, true);
            EditorGUILayout.PropertyField(_leftPaddingProperty, true);
            EditorGUILayout.PropertyField(_rightPaddingProperty, true);
            EditorGUILayout.PropertyField(_topPaddingProperty, true);
            EditorGUILayout.PropertyField(_bottomPaddingProperty, true);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(
                _overwriteOriginalsProperty,
                new GUIContent("Overwrite Originals")
            );
            using (new EditorGUI.DisabledScope(_overwriteOriginals))
            {
                EditorGUILayout.PropertyField(
                    _outputDirectoryProperty,
                    new GUIContent("Output Directory (optional)")
                );
            }
            EditorGUILayout.PropertyField(
                _outputReadabilityProperty,
                new GUIContent("Output Readability")
            );
            EditorGUILayout.PropertyField(
                _copyDefaultPlatformSettingsProperty,
                new GUIContent("Copy Default Platform Settings")
            );
            _serializedObject.ApplyModifiedProperties();

            _leftPadding = Mathf.Max(0, _leftPadding);
            _rightPadding = Mathf.Max(0, _rightPadding);
            _topPadding = Mathf.Max(0, _topPadding);
            _bottomPadding = Mathf.Max(0, _bottomPadding);

            if (GUILayout.Button("Find Sprites To Process"))
            {
                FindFilesToProcess();
            }

            if (_filesToProcess is { Count: > 0 })
            {
                GUILayout.Label(
                    $"Found {_filesToProcess.Count} sprites to process.",
                    EditorStyles.boldLabel
                );
                if (0 < _multiSpriteFiles.Count)
                {
                    EditorGUILayout.HelpBox(
                        $"Detected {_multiSpriteFiles.Count} textures with Sprite Import Mode = Multiple. SpriteCropper only supports Single sprites. These will be skipped.",
                        MessageType.Warning
                    );
                    if (GUILayout.Button("Log details of Multiple-sprite textures"))
                    {
                        foreach (string path in _multiSpriteFiles)
                        {
                            this.LogWarn($"Multiple-sprite texture detected (skipped): {path}");
                        }
                    }
                }
                if (GUILayout.Button($"Process {_filesToProcess.Count} Sprites"))
                {
                    ProcessFoundSprites();
                    _filesToProcess = null;
                }
            }
            else if (_filesToProcess != null)
            {
                GUILayout.Label(
                    "No sprites found to process in the selected directories.",
                    EditorStyles.label
                );
            }

            EditorGUILayout.Space();

            using (new GUILayout.VerticalScope("box"))
            {
                Color prev = GUI.color;
                GUI.color = Color.red;
                GUILayout.Label(
                    "Danger Zone: Replace references to originals with Cropped_* versions",
                    EditorStyles.boldLabel
                );
                GUI.color = prev;
                EditorGUILayout.HelpBox(
                    "This will scan assets and replace Sprite references pointing to original textures with references to their Cropped_* counterparts. This is potentially destructive. Ensure you have backups/version control.",
                    MessageType.Error
                );
                _ackDanger = EditorGUILayout.ToggleLeft(
                    "I understand the risks and want to proceed.",
                    _ackDanger
                );
                using (new EditorGUI.DisabledScope(!_ackDanger || _overwriteOriginals))
                {
                    if (GUILayout.Button("Replace Sprite References With Cropped_* Versions"))
                    {
                        ReplaceSpriteReferencesWithCropped();
                    }
                }
            }
        }

        private void ReplaceSpriteReferencesWithCropped()
        {
            using PooledResource<List<string>> folderLease = Buffers<string>.List.Get(
                out List<string> folders
            );
            if (_inputDirectories != null)
            {
                foreach (Object maybeDirectory in _inputDirectories)
                {
                    if (maybeDirectory != null)
                    {
                        folders.Add(AssetDatabase.GetAssetPath(maybeDirectory));
                    }
                }
            }

            string outputFolder = null;
            if (_outputDirectory != null)
            {
                string selectedFolder = AssetDatabase.GetAssetPath(_outputDirectory);
                if (AssetDatabase.IsValidFolder(selectedFolder))
                {
                    outputFolder = selectedFolder;
                }
            }

            SpriteReferenceReplacementResult result;
            try
            {
                result = SpriteCropperAPI.ReplaceReferences(
                    folders,
                    applyChanges: true,
                    cancelRequested: (index, total) =>
                        Utils.EditorUi.CancelableProgress(
                            "Replacing Sprite References",
                            $"Scanning {index + 1}/{total}",
                            total == 0 ? 0f : index / (float)total
                        ),
                    outputFolder: outputFolder,
                    overwriteOriginals: _overwriteOriginals
                );
            }
            finally
            {
                Utils.EditorUi.ClearProgress();
            }

            foreach (string error in result.Errors)
            {
                this.LogError($"{error}");
            }
            if (result.Canceled)
            {
                this.LogWarn($"Reference replacement cancelled by user.");
            }
            this.Log(
                $"Reference replacement complete. Modified assets: {result.ModifiedAssets}. Matched references: {result.MatchedReferences}."
            );
        }

        private TextureImporter ProcessSprite(
            string assetPath,
            out ProcessOutcome outcome,
            Dictionary<string, bool> originalReadable
        )
        {
            string outputFolder = null;
            if (!_overwriteOriginals && _outputDirectory != null)
            {
                string selectedFolder = AssetDatabase.GetAssetPath(_outputDirectory);
                if (AssetDatabase.IsValidFolder(selectedFolder))
                {
                    outputFolder = selectedFolder;
                }
            }

            SpriteCropperAPI.CropOptions options = new()
            {
                LeftPadding = _leftPadding,
                RightPadding = _rightPadding,
                TopPadding = _topPadding,
                BottomPadding = _bottomPadding,
                OnlyNecessary = _onlyNecessary,
                OverwriteOriginals = _overwriteOriginals,
                OutputFolder = outputFolder,
                OutputReadability = (SpriteCropperAPI.OutputReadability)_outputReadability,
                CopyDefaultPlatformSettings = _copyDefaultPlatformSettings,
            };
            bool sourceWasReadable = originalReadable.TryGetValue(assetPath, out bool wasReadable)
                ? wasReadable
                : true;
            SpriteCropperAPI.CropResult result = SpriteCropperAPI.CropPrepared(
                assetPath,
                options,
                sourceWasReadable
            );
            if (!string.IsNullOrWhiteSpace(result.Warning))
            {
                this.LogWarn($"{result.Warning}");
            }
            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                this.LogWarn($"{result.Error}");
            }
            outcome = (ProcessOutcome)result.Status;
            return result.OutputImporter;
        }

        internal enum OutputReadability
        {
            MirrorSource = 0,
            Readable = 1,
            NotReadable = 2,
        }

        private enum ProcessOutcome
        {
            Success,
            SkippedNoChange,
            RetryableError,
            FatalError,
        }

        private sealed class VisibleBoundsScanJob
        {
            internal VisibleBounds Bounds => new(_hasVisible, _minX, _minY, _maxX, _maxY);

            private readonly Color32[] _pixels;
            private readonly int _width;
            private readonly int _height;
            private readonly byte _alphaByteThreshold;
            private readonly object _lockObject = new();
            private bool _hasVisible;
            private int _minX;
            private int _minY;
            private int _maxX;
            private int _maxY;

            internal VisibleBoundsScanJob(
                Color32[] pixels,
                int width,
                int height,
                byte alphaByteThreshold
            )
            {
                _pixels = pixels;
                _width = width;
                _height = height;
                _alphaByteThreshold = alphaByteThreshold;
                _minX = width;
                _minY = height;
            }

            internal VisibleBounds CreateLocalBounds()
            {
                return new VisibleBounds(false, _width, _height, 0, 0);
            }

            internal VisibleBounds ScanRow(
                int y,
                ParallelLoopState loopState,
                VisibleBounds localBounds
            )
            {
                int minX = _width;
                int maxX = 0;
                bool hasVisible = false;
                int rowStart = y * _width;
                for (int x = 0; x < _width; ++x)
                {
                    if (_pixels[rowStart + x].a <= _alphaByteThreshold)
                    {
                        continue;
                    }

                    hasVisible = true;
                    minX = Mathf.Min(minX, x);
                    maxX = Mathf.Max(maxX, x);
                }

                if (!hasVisible)
                {
                    return localBounds;
                }

                return new VisibleBounds(
                    true,
                    Mathf.Min(localBounds.MinX, minX),
                    Mathf.Min(localBounds.MinY, y),
                    Mathf.Max(localBounds.MaxX, maxX),
                    Mathf.Max(localBounds.MaxY, y)
                );
            }

            internal void MergeLocalBounds(VisibleBounds localBounds)
            {
                if (!localBounds.HasVisible)
                {
                    return;
                }

                lock (_lockObject)
                {
                    _hasVisible = true;
                    _minX = Mathf.Min(_minX, localBounds.MinX);
                    _minY = Mathf.Min(_minY, localBounds.MinY);
                    _maxX = Mathf.Max(_maxX, localBounds.MaxX);
                    _maxY = Mathf.Max(_maxY, localBounds.MaxY);
                }
            }
        }

        private sealed class CropRowCopyJob
        {
            private readonly Color32[] _pixels;
            private readonly int _width;
            private readonly int _height;
            private readonly int _visibleMinY;
            private readonly int _srcX0;
            private readonly int _srcY0;
            private readonly int _srcY1;
            private readonly int _cropWidth;
            private readonly int _leftClear;
            private readonly int _rightClear;
            private readonly int _copyStartDestX;
            private readonly int _copyCount;
            private readonly Color32[] _croppedPixels;

            internal CropRowCopyJob(
                Color32[] pixels,
                int width,
                int height,
                int visibleMinY,
                int srcX0,
                int srcY0,
                int srcY1,
                int cropWidth,
                int leftClear,
                int rightClear,
                int copyStartDestX,
                int copyCount,
                Color32[] croppedPixels
            )
            {
                _pixels = pixels;
                _width = width;
                _height = height;
                _visibleMinY = visibleMinY;
                _srcX0 = srcX0;
                _srcY0 = srcY0;
                _srcY1 = srcY1;
                _cropWidth = cropWidth;
                _leftClear = leftClear;
                _rightClear = rightClear;
                _copyStartDestX = copyStartDestX;
                _copyCount = copyCount;
                _croppedPixels = croppedPixels;
            }

            internal void Execute(int y)
            {
                CopyCropRow(
                    y,
                    _pixels,
                    _width,
                    _height,
                    _visibleMinY,
                    _srcX0,
                    _srcY0,
                    _srcY1,
                    _cropWidth,
                    _leftClear,
                    _rightClear,
                    _copyStartDestX,
                    _copyCount,
                    _croppedPixels
                );
            }
        }

        /// <summary>
        /// Pure result of the crop geometry computation (no Unity asset I/O), so the
        /// dimension/padding/pivot behavior can be unit-tested without importing textures.
        /// </summary>
        internal readonly struct CropComputation
        {
            public readonly bool HasVisible;
            public readonly bool ShouldSkipNoChange;
            public readonly int VisibleMinX;
            public readonly int VisibleMinY;
            public readonly int VisibleMaxX;
            public readonly int VisibleMaxY;
            public readonly int CropWidth;
            public readonly int CropHeight;
            public readonly Vector2 NewPivot;

            public CropComputation(
                bool hasVisible,
                bool shouldSkipNoChange,
                int visibleMinX,
                int visibleMinY,
                int visibleMaxX,
                int visibleMaxY,
                int cropWidth,
                int cropHeight,
                Vector2 newPivot
            )
            {
                HasVisible = hasVisible;
                ShouldSkipNoChange = shouldSkipNoChange;
                VisibleMinX = visibleMinX;
                VisibleMinY = visibleMinY;
                VisibleMaxX = visibleMaxX;
                VisibleMaxY = visibleMaxY;
                CropWidth = cropWidth;
                CropHeight = cropHeight;
                NewPivot = newPivot;
            }
        }

        internal readonly struct VisibleBounds
        {
            internal readonly bool HasVisible;
            internal readonly int MinX;
            internal readonly int MinY;
            internal readonly int MaxX;
            internal readonly int MaxY;

            internal VisibleBounds(bool hasVisible, int minX, int minY, int maxX, int maxY)
            {
                HasVisible = hasVisible;
                MinX = minX;
                MinY = minY;
                MaxX = maxX;
                MaxY = maxY;
            }
        }
    }
#endif
}
