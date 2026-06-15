// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;
    using CustomEditors;
    using WallstopStudios.UnityHelpers.Core.Extension;
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
        private const string Name = "Sprite Cropper";

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

        private const float AlphaThreshold = 0.01f;

        internal enum OutputReadability
        {
            MirrorSource = 0,
            Readable = 1,
            NotReadable = 2,
        }

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

        private Regex _regex;

        // Diagnostics for Multiple-sprite textures detected during search
        private readonly List<string> _multiSpriteFiles = new();

        // Danger zone acknowledgment for reference replacement
        private bool _ackDanger;

        [MenuItem("Tools/Wallstop Studios/Unity Helpers/" + Name)]
        private static void ShowWindow() => GetWindow<SpriteCropper>(Name);

        private void OnEnable()
        {
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

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Input directories", EditorStyles.boldLabel);
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

            // Clamp paddings to non-negative
            _leftPadding = Mathf.Max(0, _leftPadding);
            _rightPadding = Mathf.Max(0, _rightPadding);
            _topPadding = Mathf.Max(0, _topPadding);
            _bottomPadding = Mathf.Max(0, _bottomPadding);

            if (GUILayout.Button("Find Sprites To Process"))
            {
                _regex = !string.IsNullOrWhiteSpace(_spriteNameRegex)
                    ? new Regex(_spriteNameRegex)
                    : null;
                _multiSpriteFiles.Clear();
                FindFilesToProcess();
            }

            if (_filesToProcess is { Count: > 0 })
            {
                GUILayout.Label(
                    $"Found {_filesToProcess.Count} sprites to process.",
                    EditorStyles.boldLabel
                );
                if (_multiSpriteFiles.Count > 0)
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
            // Danger Zone: Reference Replacement
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
                using (new EditorGUI.DisabledScope(!_ackDanger))
                {
                    if (GUILayout.Button("Replace Sprite References With Cropped_* Versions"))
                    {
                        ReplaceSpriteReferencesWithCropped();
                    }
                }
            }
        }

        internal void FindFilesToProcess()
        {
            _filesToProcess ??= new List<string>();
            _filesToProcess.Clear();
            if (_inputDirectories is not { Count: > 0 })
            {
                this.LogWarn($"No input directories selected.");
                return;
            }

            foreach (Object maybeDirectory in _inputDirectories.Where(d => d != null))
            {
                string assetPath = AssetDatabase.GetAssetPath(maybeDirectory);
                if (!AssetDatabase.IsValidFolder(assetPath))
                {
                    this.LogWarn($"Skipping invalid path: {assetPath}");
                    continue;
                }

                IEnumerable<string> files = Directory
                    .GetFiles(assetPath, "*.*", SearchOption.AllDirectories)
                    .Where(file =>
                        Array.Exists(
                            ImageFileExtensions,
                            extension =>
                                file.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                        )
                    );

                foreach (string file in files)
                {
                    if (file.Contains(CroppedPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string fileName = Path.GetFileNameWithoutExtension(file);
                    if (_regex != null && !_regex.IsMatch(fileName))
                    {
                        continue;
                    }

                    // Skip and record textures with Multiple sprite import mode
                    if (AssetImporter.GetAtPath(file) is TextureImporter ti)
                    {
                        if (
                            ti.textureType == TextureImporterType.Sprite
                            && ti.spriteImportMode != SpriteImportMode.Single
                        )
                        {
                            _multiSpriteFiles.Add(file);
                            continue;
                        }
                    }

                    _filesToProcess.Add(file);
                }
            }
            Repaint();
        }

        private void ReplaceSpriteReferencesWithCropped()
        {
            try
            {
                // Build mapping from original Sprite to Cropped_* Sprite based on input directories
                Dictionary<Sprite, Sprite> mapping = new();
                if (_inputDirectories is not { Count: > 0 })
                {
                    this.LogWarn(
                        $"No input directories selected; cannot build replacement mapping."
                    );
                    return;
                }

                foreach (Object maybeDirectory in _inputDirectories.Where(d => d != null))
                {
                    string dirPath = AssetDatabase.GetAssetPath(maybeDirectory);
                    if (!AssetDatabase.IsValidFolder(dirPath))
                    {
                        continue;
                    }

                    IEnumerable<string> files = Directory
                        .GetFiles(dirPath, "*.*", SearchOption.AllDirectories)
                        .Where(file =>
                            Array.Exists(
                                ImageFileExtensions,
                                extension =>
                                    file.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                            )
                        );

                    foreach (string file in files)
                    {
                        if (file.Contains(CroppedPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        string croppedPath = Path.Combine(
                            Path.GetDirectoryName(file) ?? string.Empty,
                            CroppedPrefix + Path.GetFileName(file)
                        );
                        if (!File.Exists(croppedPath))
                        {
                            continue;
                        }

                        Sprite original = AssetDatabase.LoadAssetAtPath<Sprite>(file);
                        Sprite cropped = AssetDatabase.LoadAssetAtPath<Sprite>(croppedPath);
                        if (original != null && cropped != null)
                        {
                            mapping[original] = cropped;
                        }
                    }
                }

                if (mapping.Count == 0)
                {
                    this.LogWarn(
                        $"No original→Cropped_* sprite pairs found. Aborting replacement."
                    );
                    return;
                }

                string[] allAssets = AssetDatabase.GetAllAssetPaths();
                string[] candidateExts =
                {
                    ".prefab",
                    ".unity",
                    ".asset",
                    ".mat",
                    ".anim",
                    ".overrideController",
                };
                int modifiedAssets = 0;

                using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: true))
                {
                    try
                    {
                        for (int i = 0; i < allAssets.Length; ++i)
                        {
                            string path = allAssets[i];
                            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                            if (
                                !candidateExts.Any(ext =>
                                    path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)
                                )
                            )
                            {
                                continue;
                            }

                            if (
                                Utils.EditorUi.CancelableProgress(
                                    "Replacing Sprite References",
                                    $"Scanning {i + 1}/{allAssets.Length}: {Path.GetFileName(path)}",
                                    i / (float)allAssets.Length
                                )
                            )
                            {
                                this.LogWarn($"Reference replacement cancelled by user.");
                                break;
                            }

                            bool assetModified = false;
                            Object[] objs = AssetDatabase.LoadAllAssetsAtPath(path);
                            foreach (Object o in objs)
                            {
                                if (o == null)
                                {
                                    continue;
                                }
                                bool objectModified = false;
                                SerializedObject so = new(o);
                                SerializedProperty it = so.GetIterator();
                                bool enter = true;
                                while (it.NextVisible(enter))
                                {
                                    enter = false;
                                    if (it.propertyType == SerializedPropertyType.ObjectReference)
                                    {
                                        Sprite s = it.objectReferenceValue as Sprite;
                                        if (
                                            s != null
                                            && mapping.TryGetValue(s, out Sprite replacement)
                                        )
                                        {
                                            it.objectReferenceValue = replacement;
                                            objectModified = true;
                                        }
                                    }
                                }
                                if (objectModified)
                                {
                                    so.ApplyModifiedProperties();
                                    EditorUtility.SetDirty(o);
                                    assetModified = true;
                                }
                            }
                            if (assetModified)
                            {
                                modifiedAssets++;
                            }
                        }
                    }
                    finally
                    {
                        AssetDatabase.SaveAssets();
                        Utils.EditorUi.ClearProgress();
                    }
                }

                this.Log(
                    $"Reference replacement complete. Modified assets: {modifiedAssets}. Mapped pairs: {mapping.Count}."
                );
            }
            catch (Exception e)
            {
                this.LogError($"Error during reference replacement.", e);
            }
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
                                        // No-op
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

                    if (!canceled && needReprocessing.Any())
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

                    // Restore readability to originals that we changed
                    if (originalReadable.Count > 0 && !_overwriteOriginals)
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

            // Make readable if needed and remember original state to restore after processing
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

        private enum ProcessOutcome
        {
            Success,
            SkippedNoChange,
            RetryableError,
            FatalError,
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
            int minX = width;
            int minY = height;
            int maxX = 0;
            int maxY = 0;
            bool hasVisible = false;
            object lockObject = new();
            byte alphaByteThreshold = (byte)
                Mathf.Clamp(Mathf.RoundToInt(alphaThreshold * 255f), 0, 255);
            Parallel.For(
                0,
                width * height,
                () => (minX: width, minY: height, maxX: 0, maxY: 0, hasVisible: false),
                (index, _, localState) =>
                {
                    int x = index % width;
                    int y = index / width;

                    byte a = pixels[index].a;
                    if (a > alphaByteThreshold)
                    {
                        localState.hasVisible = true;
                        localState.minX = Mathf.Min(localState.minX, x);
                        localState.minY = Mathf.Min(localState.minY, y);
                        localState.maxX = Mathf.Max(localState.maxX, x);
                        localState.maxY = Mathf.Max(localState.maxY, y);
                    }
                    return localState;
                },
                finalLocalState =>
                {
                    if (finalLocalState.hasVisible)
                    {
                        lock (lockObject)
                        {
                            hasVisible = true;
                            minX = Mathf.Min(minX, finalLocalState.minX);
                            minY = Mathf.Min(minY, finalLocalState.minY);
                            maxX = Mathf.Max(maxX, finalLocalState.maxX);
                            maxY = Mathf.Max(maxY, finalLocalState.maxY);
                        }
                    }
                }
            );

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
                cropWidth > 0 ? newPivotPixels.x / cropWidth : 0.5f,
                cropHeight > 0 ? newPivotPixels.y / cropHeight : 0.5f
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

        private TextureImporter ProcessSprite(
            string assetPath,
            out ProcessOutcome outcome,
            Dictionary<string, bool> originalReadable
        )
        {
            outcome = ProcessOutcome.FatalError;
            string assetDirectory = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrWhiteSpace(assetDirectory))
            {
                outcome = ProcessOutcome.FatalError;
                return null;
            }

            if (
                AssetImporter.GetAtPath(assetPath)
                is not TextureImporter { textureType: TextureImporterType.Sprite } importer
            )
            {
                outcome = ProcessOutcome.FatalError;
                return null;
            }

            if (importer.spriteImportMode != SpriteImportMode.Single)
            {
                this.LogWarn($"Skipping texture with Multiple sprite mode: {assetPath}");
                outcome = ProcessOutcome.SkippedNoChange;
                return null;
            }

            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (tex == null)
            {
                outcome = ProcessOutcome.RetryableError;
                return null;
            }

            Color32[] pixels = tex.GetPixels32();
            int width = tex.width;
            int height = tex.height;
            CropComputation crop = ComputeCrop(
                pixels,
                width,
                height,
                _leftPadding,
                _rightPadding,
                _topPadding,
                _bottomPadding,
                AlphaThreshold,
                GetSpritePivot(importer),
                _onlyNecessary
            );

            if (crop.ShouldSkipNoChange)
            {
                outcome = ProcessOutcome.SkippedNoChange;
                return null;
            }

            int visibleMinX = crop.VisibleMinX;
            int visibleMinY = crop.VisibleMinY;
            int visibleMaxX = crop.VisibleMaxX;
            int visibleMaxY = crop.VisibleMaxY;
            int cropWidth = crop.CropWidth;
            int cropHeight = crop.CropHeight;

            Texture2D cropped = new(cropWidth, cropHeight, TextureFormat.RGBA32, false);
            int pixelCount = cropWidth * cropHeight;
            // Note: We need an exact-size array for SetPixels32, which requires the array length
            // to exactly match width * height. SystemArrayPool returns arrays that may be larger
            // than requested, so we must allocate an exact-size array for the Unity API call.
            Color32[] croppedPixels = new Color32[pixelCount];

            int srcX0 = Mathf.Max(visibleMinX, 0);
            int srcY0 = Mathf.Max(visibleMinY, 0);
            int srcX1 = Mathf.Min(visibleMaxX, width - 1);
            int srcY1 = Mathf.Min(visibleMaxY, height - 1);

            Parallel.For(
                0,
                cropHeight,
                y =>
                {
                    int destRow = y * cropWidth;
                    int srcY = visibleMinY + y;
                    if (srcY < 0 || srcY >= height || srcY < srcY0 || srcY > srcY1)
                    {
                        Array.Clear(croppedPixels, destRow, cropWidth);
                        return;
                    }

                    int copyStartDestX = Mathf.Max(0, srcX0 - visibleMinX);
                    int copyEndDestX = Mathf.Min(cropWidth - 1, srcX1 - visibleMinX);

                    int leftClear = copyStartDestX;
                    int rightClear = cropWidth - 1 - copyEndDestX;

                    if (leftClear > 0)
                    {
                        Array.Clear(croppedPixels, destRow, leftClear);
                    }

                    if (copyEndDestX >= copyStartDestX)
                    {
                        int numToCopy = copyEndDestX - copyStartDestX + 1;
                        int srcStartX = srcX0;
                        int srcIndex = srcY * width + srcStartX;
                        int destIndex = destRow + copyStartDestX;
                        Array.Copy(pixels, srcIndex, croppedPixels, destIndex, numToCopy);
                    }

                    if (rightClear > 0)
                    {
                        Array.Clear(croppedPixels, destRow + (cropWidth - rightClear), rightClear);
                    }
                }
            );

            cropped.SetPixels32(croppedPixels);
            cropped.Apply();

            // Determine output path and importer to modify
            string outputDirectory = assetDirectory;
            if (!_overwriteOriginals && _outputDirectory != null)
            {
                string dirPath = AssetDatabase.GetAssetPath(_outputDirectory);
                if (!string.IsNullOrWhiteSpace(dirPath) && AssetDatabase.IsValidFolder(dirPath))
                {
                    outputDirectory = dirPath;
                }
            }

            string outputFileName = _overwriteOriginals
                ? Path.GetFileName(assetPath)
                : CroppedPrefix + Path.GetFileName(assetPath);
            string newPath = Path.Combine(outputDirectory, outputFileName);

            byte[] pngBytes = cropped.EncodeToPNG();
            File.WriteAllBytes(newPath, pngBytes);
            DestroyImmediate(cropped);
            AssetDatabase.ImportAsset(newPath);

            TextureImporter newImporter = AssetImporter.GetAtPath(newPath) as TextureImporter;
            if (newImporter == null)
            {
                outcome = ProcessOutcome.RetryableError;
                return null;
            }

            TextureImporterSettings newSettings = new();
            importer.ReadTextureSettings(newSettings);
            Vector2 newPivotNorm = crop.NewPivot;

            // Adjust 9-slice borders based on trimming from edges
            Vector4 border = newSettings.spriteBorder;
            int deltaLeft = visibleMinX; // pixels trimmed from left of full image
            int deltaBottom = visibleMinY; // trimmed from bottom
            int deltaRight = width - 1 - visibleMaxX; // trimmed from right
            int deltaTop = height - 1 - visibleMaxY; // trimmed from top
            border.x = Mathf.Max(0, border.x - deltaLeft);
            border.y = Mathf.Max(0, border.y - deltaBottom);
            border.z = Mathf.Max(0, border.z - deltaRight);
            border.w = Mathf.Max(0, border.w - deltaTop);

            newSettings.spritePivot = newPivotNorm;
            newSettings.spriteAlignment = (int)SpriteAlignment.Custom;
            newSettings.spriteBorder = border;
            newImporter.SetTextureSettings(newSettings);
            // Always import the cropped output as Single unless we implement full metadata migration
            newImporter.spriteImportMode = SpriteImportMode.Single;
            newImporter.spritePivot = newPivotNorm;
            newImporter.textureType = importer.textureType;
            newImporter.filterMode = importer.filterMode;
            newImporter.textureCompression = importer.textureCompression;
            newImporter.wrapMode = importer.wrapMode;
            newImporter.mipmapEnabled = importer.mipmapEnabled;
            newImporter.spritePixelsPerUnit = importer.spritePixelsPerUnit;

            // Copy default platform settings if requested
            if (_copyDefaultPlatformSettings)
            {
                try
                {
                    TextureImporterPlatformSettings srcDefault =
                        importer.GetDefaultPlatformTextureSettings();
                    if (!string.IsNullOrWhiteSpace(srcDefault.name))
                    {
                        newImporter.SetPlatformTextureSettings(srcDefault);
                    }
                }
                catch (Exception e)
                {
                    this.LogWarn($"Failed to copy default platform settings for '{assetPath}'.", e);
                }
            }

            // Set readability based on option
            bool srcOriginalReadable = originalReadable.TryGetValue(assetPath, out bool wasReadable)
                ? wasReadable
                : importer.isReadable;
            switch (_outputReadability)
            {
                case OutputReadability.MirrorSource:
                    newImporter.isReadable = srcOriginalReadable;
                    break;
                case OutputReadability.Readable:
                    newImporter.isReadable = true;
                    break;
                case OutputReadability.NotReadable:
                    newImporter.isReadable = false;
                    break;
            }
            newImporter.SaveAndReimport();

            outcome = ProcessOutcome.Success;
            return newImporter;
        }

        private static Vector2 GetSpritePivot(TextureImporter importer)
        {
            if (importer.spriteImportMode == SpriteImportMode.Single)
            {
                return importer.spritePivot;
            }

            return new Vector2(0.5f, 0.5f);
        }
    }
#endif
}
