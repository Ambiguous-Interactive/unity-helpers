// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using CustomEditors;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Extracts individual sprites from sprite sheet textures (textures with SpriteImportMode.Multiple)
    /// and saves them as separate PNG files. Provides preview GUI with reordering, bulk operations,
    /// and optional reference replacement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Problems this solves: splitting sprite sheets into individual assets for easier management,
    /// creating separate sprites for animation systems that expect individual files, preparing
    /// assets for different build targets.
    /// </para>
    /// <para>
    /// How it works: scans input directories for textures with SpriteImportMode.Multiple, reads
    /// sprite metadata (rects, names, pivots, borders), extracts pixel data for each sprite,
    /// and writes individual PNG files. Optionally updates references in prefabs and scenes.
    /// </para>
    /// <para>
    /// Usage:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Open via menu: Tools/Wallstop Studios/Unity Helpers/Sprite Sheet Extractor.</description></item>
    /// <item><description>Select input folders and optional regex filter.</description></item>
    /// <item><description>Choose output directory and extraction options.</description></item>
    /// <item><description>Preview sprites and adjust selection/naming as needed.</description></item>
    /// <item><description>Click Extract to generate individual sprite files.</description></item>
    /// </list>
    /// <para>
    /// Pros: batch processing, preserves import settings, preview before extraction, undo support
    /// for reference replacement. Caveats: extraction is one-way; reference replacement is
    /// potentially destructive—use VCS.
    /// </para>
    /// </remarks>
    public sealed class SpriteSheetExtractor : EditorWindow
    {
        private const string Name = "Sprite Sheet Extractor";

        /// <summary>
        /// Controls whether diagnostic logging is enabled.
        /// Set to true for debugging sprite regeneration and cache issues.
        /// </summary>
        /// <remarks>
        /// Using static readonly instead of const to avoid CS0162 unreachable code warnings
        /// when the value is false, while still allowing JIT optimization.
        /// </remarks>
        private static readonly bool DiagnosticsEnabled = false;

        /// <summary>
        /// Minimum score threshold for boundary transparency detection.
        /// Lowered from 0.5 to 0.15 to handle sprite sheets with thin transparent gutters.
        /// </summary>
        private const float MinimumBoundaryScore = 0.15f;

        /// <summary>
        /// Maximum number of entries to keep fully cached with sprites.
        /// Entries beyond this limit are evicted using LRU policy.
        /// </summary>
        private const int MaxCachedEntries = 50;

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
        /// Common sprite cell sizes for grid detection candidate generation.
        /// Avoids allocation during DetectOptimalGridFromTransparency calls.
        /// </summary>
        private static readonly int[] CommonCellSizes =
        {
            8,
            16,
            24,
            32,
            48,
            64,
            96,
            128,
            256,
            512,
        };

        private static readonly Vector2 CenterPivot = new(0.5f, 0.5f);

        /// <summary>
        /// Color for sheet-level pivot markers (gold/yellow to differentiate from per-sprite markers).
        /// </summary>
        private static readonly Color SheetPivotColor = new Color(1f, 0.84f, 0f, 0.8f);

        /// <summary>
        /// EditorPrefs key for persisting splitter position.
        /// </summary>
        private const string SplitterPositionPrefsKey =
            "WallstopStudios.UnityHelpers.SpriteSheetExtractor.SplitterPosition";

        /// <summary>
        /// Minimum height for the settings section (Input/Output/Discovery).
        /// </summary>
        private const float MinSettingsHeight = 100f;

        /// <summary>
        /// Minimum height for the preview section.
        /// </summary>
        private const float MinPreviewHeight = 150f;

        /// <summary>
        /// Height of the splitter bar in pixels.
        /// </summary>
        private const float SplitterHeight = 5f;

        /// <summary>
        /// Default splitter position as ratio of window height (0.4 = 40% settings, 60% preview).
        /// </summary>
        private const float DefaultSplitterRatio = 0.4f;

        /// <summary>
        /// Represents a discovered sprite sheet with its metadata.
        /// </summary>
        public sealed class SpriteSheetEntry
        {
            internal string _assetPath;
            internal Texture2D _texture;
            internal TextureImporter _importer;
            internal SpriteImportMode _importMode;
            internal List<SpriteEntryData> _sprites;
            internal bool _isExpanded;
            internal bool _isSelected;

            internal bool _useGlobalSettings = true;
            internal bool _perSheetSettingsFoldout;
            internal ExtractionMode? _extractionModeOverride;
            internal GridSizeMode? _gridSizeModeOverride;
            internal int? _gridColumnsOverride;
            internal int? _gridRowsOverride;
            internal int? _cellWidthOverride;
            internal int? _cellHeightOverride;
            internal int? _paddingLeftOverride;
            internal int? _paddingRightOverride;
            internal int? _paddingTopOverride;
            internal int? _paddingBottomOverride;
            internal float? _alphaThresholdOverride;
            internal bool? _showOverlayOverride;
            internal bool _sourcePreviewExpanded;

            internal PivotMode? _pivotModeOverride;
            internal Vector2? _customPivotOverride;
            internal AutoDetectionAlgorithm? _autoDetectionAlgorithmOverride;
            internal int? _expectedSpriteCountOverride;

            /// <summary>
            /// Per-sheet override for snap to texture divisor. Only used when _useGlobalSettings is false.
            /// </summary>
            internal bool? _snapToTextureDivisorOverride;

            /// <summary>
            /// Whether to use a per-sheet pivot marker color override.
            /// </summary>
            internal bool _usePivotMarkerColorOverride;

            /// <summary>
            /// Per-sheet pivot marker color override.
            /// UI-only preference; not saved to per-sheet config files.
            /// </summary>
            internal Color _pivotMarkerColorOverride = Color.cyan;

            /// <summary>
            /// When enabled, allows interactive pivot editing via click/drag in the source texture preview.
            /// </summary>
            internal bool _editPivotsMode;

            internal SpriteSheetConfig _loadedConfig;
            internal bool _configLoaded;
            internal bool _configStale;
            internal SpriteSheetAlgorithms.AlgorithmResult? _cachedAlgorithmResult;
            internal string _lastAlgorithmDisplayText;

            /// <summary>
            /// The last computed cache key used to detect when sprite bounds need regeneration.
            /// </summary>
            internal int _lastCacheKey;

            /// <summary>
            /// Indicates whether the sprite bounds need regeneration due to settings changes.
            /// </summary>
            internal bool _needsRegeneration;

            /// <summary>
            /// The last access time (ticks) for LRU cache eviction.
            /// </summary>
            internal long _lastAccessTime;

            /// <summary>
            /// Computes a composite cache key based on all settings that affect sprite bounds calculation.
            /// Used to detect when cached sprite data is stale and needs regeneration.
            /// </summary>
            /// <param name="extractor">The SpriteSheetExtractor instance to read global settings from.</param>
            /// <returns>A hash code representing the current configuration state.</returns>
            internal int GetBoundsCacheKey(SpriteSheetExtractor extractor)
            {
                if (extractor == null)
                {
                    return 0;
                }

                ExtractionMode effectiveExtractionMode = extractor.GetEffectiveExtractionMode(this);
                GridSizeMode effectiveGridSizeMode = extractor.GetEffectiveGridSizeMode(this);
                int effectiveGridColumns = extractor.GetEffectiveGridColumns(this);
                int effectiveGridRows = extractor.GetEffectiveGridRows(this);
                int effectiveCellWidth = extractor.GetEffectiveCellWidth(this);
                int effectiveCellHeight = extractor.GetEffectiveCellHeight(this);
                int effectivePaddingLeft = extractor.GetEffectivePaddingLeft(this);
                int effectivePaddingRight = extractor.GetEffectivePaddingRight(this);
                int effectivePaddingTop = extractor.GetEffectivePaddingTop(this);
                int effectivePaddingBottom = extractor.GetEffectivePaddingBottom(this);
                float effectiveAlphaThreshold = extractor.GetEffectiveAlphaThreshold(this);
                AutoDetectionAlgorithm effectiveAlgorithm =
                    extractor.GetEffectiveAutoDetectionAlgorithm(this);
                int effectiveExpectedCount = extractor.GetEffectiveExpectedSpriteCount(this);
                bool effectiveSnapToDivisor = extractor.GetEffectiveSnapToTextureDivisor(this);

                int textureWidth = _texture != null ? _texture.width : 0;
                int textureHeight = _texture != null ? _texture.height : 0;

                return Objects.HashCode(
                    effectiveExtractionMode,
                    effectiveGridSizeMode,
                    effectiveGridColumns,
                    effectiveGridRows,
                    effectiveCellWidth,
                    effectiveCellHeight,
                    effectivePaddingLeft,
                    effectivePaddingRight,
                    effectivePaddingTop,
                    effectivePaddingBottom,
                    effectiveAlphaThreshold,
                    effectiveAlgorithm,
                    effectiveExpectedCount,
                    effectiveSnapToDivisor,
                    textureWidth,
                    textureHeight
                );
            }
        }

        /// <summary>
        /// Represents an individual sprite within a sprite sheet.
        /// </summary>
        internal sealed class SpriteEntryData
        {
            internal string _originalName;
            internal string _outputName;
            internal Rect _rect;
            internal Vector2 _pivot;
            internal Vector4 _border;
            internal int _sortIndex;
            internal bool _isSelected;
            internal Texture2D _previewTexture;

            /// <summary>
            /// Whether to use a per-sprite pivot override.
            /// </summary>
            internal bool _usePivotOverride;

            /// <summary>
            /// Per-sprite pivot mode override. Only used when <see cref="_usePivotOverride"/> is true.
            /// </summary>
            internal PivotMode _pivotModeOverride;

            /// <summary>
            /// Per-sprite custom pivot override. Only used when <see cref="_usePivotOverride"/> is true
            /// and <see cref="_pivotModeOverride"/> is <see cref="PivotMode.Custom"/>.
            /// </summary>
            internal Vector2 _customPivotOverride;

            /// <summary>
            /// Whether to use a per-sprite pivot marker color override.
            /// </summary>
            internal bool _usePivotColorOverride;

            /// <summary>
            /// Per-sprite pivot marker color override.
            /// UI-only preference; not saved to per-sheet config files.
            /// </summary>
            internal Color _pivotColorOverride;
        }

        /// <summary>
        /// Holds deferred import data for batch processing during sprite extraction.
        /// This allows writing all PNG files first, then batching all import operations together.
        /// </summary>
        internal readonly struct PendingImportSettings
        {
            /// <summary>
            /// The output path where the sprite was written.
            /// </summary>
            internal readonly string OutputPath;

            /// <summary>
            /// The source texture importer to copy settings from.
            /// </summary>
            internal readonly TextureImporter SourceImporter;

            /// <summary>
            /// The sprite entry data containing pivot, border, and other sprite-specific settings.
            /// </summary>
            internal readonly SpriteEntryData Sprite;

            /// <summary>
            /// The parent sheet entry for additional context.
            /// </summary>
            internal readonly SpriteSheetEntry Entry;

            internal PendingImportSettings(
                string outputPath,
                TextureImporter sourceImporter,
                SpriteEntryData sprite,
                SpriteSheetEntry entry
            )
            {
                OutputPath = outputPath;
                SourceImporter = sourceImporter;
                Sprite = sprite;
                Entry = entry;
            }
        }

        public enum SortMode
        {
            [Obsolete("Use a specific SortMode value instead of None.")]
            None = 0,
            Original = 1,
            ByName = 2,
            ByPositionTopLeft = 3,
            ByPositionBottomLeft = 4,
            Reversed = 5,
        }

        /// <summary>
        /// Determines how sprites are discovered and extracted from sprite sheets.
        /// </summary>
        public enum ExtractionMode
        {
            [Obsolete("Use a specific ExtractionMode value instead of None.")]
            None = 0,
            FromMetadata = 1,
            GridBased = 2,
            AlphaDetection = 3,
            PaddedGrid = 4,
        }

        /// <summary>
        /// Determines whether grid dimensions are calculated automatically or manually specified.
        /// </summary>
        public enum GridSizeMode
        {
            [Obsolete("Use a specific GridSizeMode value instead of None.")]
            None = 0,
            Auto = 1,
            Manual = 2,
        }

        /// <summary>
        /// Determines the size of sprite preview thumbnails.
        /// </summary>
        public enum PreviewSizeMode
        {
            [Obsolete("Use a specific PreviewSizeMode value instead of None.")]
            None = 0,
            Size24 = 1,
            Size32 = 2,
            Size64 = 3,
            RealSize = 4,
        }

        /// <summary>
        /// Identifies whether a pivot drag operation targets a per-sprite or sheet-level pivot.
        /// </summary>
        private enum PivotDragType
        {
            [Obsolete("Use a specific PivotDragType value instead of None.")]
            None = 0,
            Sprite = 1,
            Sheet = 2,
        }

        [SerializeField]
        internal List<Object> _inputDirectories = new();

        [SerializeField]
        internal string _spriteNameRegex = ".*";

        [SerializeField]
        internal Object _outputDirectory;

        [SerializeField]
        internal string _namingPrefix = "";

        [SerializeField]
        internal bool _preserveImportSettings = true;

        [SerializeField]
        internal bool _overwriteExisting;

        [SerializeField]
        internal bool _dryRun;

        [SerializeField]
        internal SortMode _sortMode = SortMode.Original;

        [SerializeField]
        internal ExtractionMode _extractionMode = ExtractionMode.FromMetadata;

        [SerializeField]
        internal GridSizeMode _gridSizeMode = GridSizeMode.Auto;

        [SerializeField]
        internal PreviewSizeMode _previewSizeMode = PreviewSizeMode.Size32;

        [SerializeField]
        internal int _gridColumns = 4;

        [SerializeField]
        internal int _gridRows = 4;

        [SerializeField]
        internal int _cellWidth = 32;

        [SerializeField]
        internal int _cellHeight = 32;

        [SerializeField]
        internal int _paddingLeft;

        [SerializeField]
        internal int _paddingRight;

        [SerializeField]
        internal int _paddingTop;

        [SerializeField]
        internal int _paddingBottom;

        [SerializeField]
        internal float _alphaThreshold = 0.01f;

        [SerializeField]
        internal bool _showOverlay;

        [SerializeField]
        internal PivotMode _pivotMode = PivotMode.Center;

        [SerializeField]
        internal Vector2 _customPivot = new(0.5f, 0.5f);

        [SerializeField]
        internal AutoDetectionAlgorithm _autoDetectionAlgorithm = AutoDetectionAlgorithm.AutoBest;

        [SerializeField]
        internal int _expectedSpriteCountHint = -1;

        /// <summary>
        /// When enabled, algorithms adjust cell sizes to be exact divisors of texture dimensions,
        /// using transparency analysis to handle remainders intelligently.
        /// </summary>
        [SerializeField]
        internal bool _snapToTextureDivisor = true;

        /// <summary>
        /// Color of the overlay lines in source texture previews.
        /// UI-only preference; not saved to per-sheet config files.
        /// </summary>
        [SerializeField]
        internal Color _overlayColor = new Color(0f, 1f, 1f, 0.5f);

        /// <summary>
        /// Color for pivot position crosshairs in sprite previews.
        /// UI-only preference; not saved to per-sheet config files.
        /// </summary>
        [SerializeField]
        internal Color _pivotMarkerColor = Color.cyan;

        [SerializeField]
        internal bool _sourcePreviewFoldout;

        [SerializeField]
        internal bool _dangerZoneFoldout;

        // Intentionally not serialized - users must re-acknowledge danger each session
        private bool _ackDanger;

        private SerializedObject _serializedObject;
        private SerializedProperty _inputDirectoriesProperty;
        private SerializedProperty _spriteNameRegexProperty;
        private SerializedProperty _outputDirectoryProperty;
        private SerializedProperty _namingPrefixProperty;
        private SerializedProperty _preserveImportSettingsProperty;
        private SerializedProperty _overwriteExistingProperty;
        private SerializedProperty _dryRunProperty;
        private SerializedProperty _sortModeProperty;
        private SerializedProperty _extractionModeProperty;
        private SerializedProperty _gridSizeModeProperty;
        private SerializedProperty _previewSizeModeProperty;
        private SerializedProperty _gridColumnsProperty;
        private SerializedProperty _gridRowsProperty;
        private SerializedProperty _cellWidthProperty;
        private SerializedProperty _cellHeightProperty;
        private SerializedProperty _paddingLeftProperty;
        private SerializedProperty _paddingRightProperty;
        private SerializedProperty _paddingTopProperty;
        private SerializedProperty _paddingBottomProperty;
        private SerializedProperty _alphaThresholdProperty;
        private SerializedProperty _showOverlayProperty;
        private SerializedProperty _pivotModeProperty;
        private SerializedProperty _customPivotProperty;
        private SerializedProperty _autoDetectionAlgorithmProperty;
        private SerializedProperty _expectedSpriteCountHintProperty;
        private SerializedProperty _snapToTextureDivisorProperty;
        private SerializedProperty _overlayColorProperty;
        private SerializedProperty _pivotMarkerColorProperty;
        private SerializedProperty _sourcePreviewFoldoutProperty;
        private SerializedProperty _dangerZoneFoldoutProperty;

        private Regex _regex;
        private string _regexError;
        private string _lastValidatedRegex;

        private SortMode _lastSortMode;
        private List<SpriteEntryData> _cachedSortedSprites;
        private List<SpriteEntryData> _lastSpritesSource;

        private PreviewSizeMode _lastPreviewSizeMode;
        private ExtractionMode _lastExtractionMode;
        private bool _lastShowOverlay;
        internal bool _previewRegenerationScheduled;
        private bool _regenerationInProgress;

        internal List<SpriteSheetEntry> _discoveredSheets;
        private Vector2 _scrollPosition;

        /// <summary>
        /// Scroll position for the settings section.
        /// </summary>
        private Vector2 _settingsScrollPosition;

        /// <summary>
        /// Current splitter position in pixels from top of content area.
        /// </summary>
        private float _splitterPosition;

        /// <summary>
        /// Whether the user is currently dragging the splitter.
        /// </summary>
        private bool _isDraggingSplitter;

        private int _lastExtractedCount;
        private int _lastSkippedCount;
        private int _lastErrorCount;

        private SpriteSheetEntry _draggedPivotTarget;
        private PivotDragType _draggedPivotType;
        private int _draggedSpriteIndex;
        private bool _isDraggingPivot;
        private SpriteSheetEntry _hoveredPivotTarget;
        private int _hoveredSpriteIndex;
        private bool _isHoveringPivot;

        internal static bool SuppressUserPrompts { get; set; }

        static SpriteSheetExtractor()
        {
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

        [MenuItem("Tools/Wallstop Studios/Unity Helpers/" + Name)]
        private static void ShowWindow() => GetWindow<SpriteSheetExtractor>(Name);

        private void OnEnable()
        {
            // Set minimum window size to prevent layout issues
            minSize = new Vector2(
                400f,
                MinSettingsHeight + MinPreviewHeight + SplitterHeight + 50f
            );

            _serializedObject = new SerializedObject(this);
            _inputDirectoriesProperty = _serializedObject.FindProperty(nameof(_inputDirectories));
            _spriteNameRegexProperty = _serializedObject.FindProperty(nameof(_spriteNameRegex));
            _outputDirectoryProperty = _serializedObject.FindProperty(nameof(_outputDirectory));
            _namingPrefixProperty = _serializedObject.FindProperty(nameof(_namingPrefix));
            _preserveImportSettingsProperty = _serializedObject.FindProperty(
                nameof(_preserveImportSettings)
            );
            _overwriteExistingProperty = _serializedObject.FindProperty(nameof(_overwriteExisting));
            _dryRunProperty = _serializedObject.FindProperty(nameof(_dryRun));
            _sortModeProperty = _serializedObject.FindProperty(nameof(_sortMode));
            _extractionModeProperty = _serializedObject.FindProperty(nameof(_extractionMode));
            _gridSizeModeProperty = _serializedObject.FindProperty(nameof(_gridSizeMode));
            _previewSizeModeProperty = _serializedObject.FindProperty(nameof(_previewSizeMode));
            _gridColumnsProperty = _serializedObject.FindProperty(nameof(_gridColumns));
            _gridRowsProperty = _serializedObject.FindProperty(nameof(_gridRows));
            _cellWidthProperty = _serializedObject.FindProperty(nameof(_cellWidth));
            _cellHeightProperty = _serializedObject.FindProperty(nameof(_cellHeight));
            _paddingLeftProperty = _serializedObject.FindProperty(nameof(_paddingLeft));
            _paddingRightProperty = _serializedObject.FindProperty(nameof(_paddingRight));
            _paddingTopProperty = _serializedObject.FindProperty(nameof(_paddingTop));
            _paddingBottomProperty = _serializedObject.FindProperty(nameof(_paddingBottom));
            _alphaThresholdProperty = _serializedObject.FindProperty(nameof(_alphaThreshold));
            _showOverlayProperty = _serializedObject.FindProperty(nameof(_showOverlay));
            _pivotModeProperty = _serializedObject.FindProperty(nameof(_pivotMode));
            _customPivotProperty = _serializedObject.FindProperty(nameof(_customPivot));
            _autoDetectionAlgorithmProperty = _serializedObject.FindProperty(
                nameof(_autoDetectionAlgorithm)
            );
            _expectedSpriteCountHintProperty = _serializedObject.FindProperty(
                nameof(_expectedSpriteCountHint)
            );
            _snapToTextureDivisorProperty = _serializedObject.FindProperty(
                nameof(_snapToTextureDivisor)
            );
            _overlayColorProperty = _serializedObject.FindProperty(nameof(_overlayColor));
            _pivotMarkerColorProperty = _serializedObject.FindProperty(nameof(_pivotMarkerColor));
            _sourcePreviewFoldoutProperty = _serializedObject.FindProperty(
                nameof(_sourcePreviewFoldout)
            );
            _dangerZoneFoldoutProperty = _serializedObject.FindProperty(nameof(_dangerZoneFoldout));

            _lastPreviewSizeMode = _previewSizeMode;
            _lastExtractionMode = _extractionMode;
            _lastShowOverlay = _showOverlay;

            // Load splitter position from EditorPrefs, defaulting to 40% of window height
            float defaultPosition =
                position.height > 0 ? position.height * DefaultSplitterRatio : 300f;
            _splitterPosition = EditorPrefs.GetFloat(SplitterPositionPrefsKey, defaultPosition);

            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            CleanupPreviewTextures();
            _cachedSortedSprites = null;
            _lastSpritesSource = null;
            _isDraggingSplitter = false;
        }

        /// <summary>
        /// Editor update callback to force continuous repainting while preview regeneration is in progress.
        /// </summary>
        private void OnEditorUpdate()
        {
            if (_regenerationInProgress)
            {
                if (DiagnosticsEnabled)
                {
                    this.Log($"OnEditorUpdate: _regenerationInProgress=true, calling Repaint()");
                }
                Repaint();
            }
        }

        private void CleanupPreviewTextures()
        {
            if (_discoveredSheets == null)
            {
                return;
            }

            for (int i = 0; i < _discoveredSheets.Count; ++i)
            {
                SpriteSheetEntry entry = _discoveredSheets[i];
                if (entry == null || entry._sprites == null)
                {
                    continue;
                }

                for (int j = 0; j < entry._sprites.Count; ++j)
                {
                    SpriteEntryData sprite = entry._sprites[j];
                    if (sprite?._previewTexture != null)
                    {
                        DestroyImmediate(sprite._previewTexture);
                        sprite._previewTexture = null;
                    }
                }
            }
        }

        private static bool IsTextureFormatSupportedForGetPixels(TextureFormat format)
        {
            switch (format)
            {
                case TextureFormat.Alpha8:
                case TextureFormat.ARGB4444:
                case TextureFormat.RGB24:
                case TextureFormat.RGBA32:
                case TextureFormat.ARGB32:
                case TextureFormat.RGB565:
                case TextureFormat.R16:
                case TextureFormat.RGBA4444:
                case TextureFormat.BGRA32:
                case TextureFormat.RHalf:
                case TextureFormat.RGHalf:
                case TextureFormat.RGBAHalf:
                case TextureFormat.RFloat:
                case TextureFormat.RGFloat:
                case TextureFormat.RGBAFloat:
                case TextureFormat.R8:
                case TextureFormat.RG16:
                case TextureFormat.RG32:
                case TextureFormat.RGB48:
                case TextureFormat.RGBA64:
                    return true;
                default:
                    return false;
            }
        }

        private void OnGUI()
        {
            _serializedObject.Update();

            // Handle splitter drag events first (before any layout)
            HandleSplitterEvents();

            // Calculate available heights
            float totalHeight = position.height;
            float settingsHeight = Mathf.Clamp(
                _splitterPosition,
                MinSettingsHeight,
                totalHeight - MinPreviewHeight - SplitterHeight
            );
            float previewHeight = Mathf.Max(
                MinPreviewHeight,
                totalHeight - settingsHeight - SplitterHeight
            );

            // Settings section (scrollable)
            using (
                EditorGUILayout.ScrollViewScope settingsScroll =
                    new EditorGUILayout.ScrollViewScope(
                        _settingsScrollPosition,
                        GUILayout.Height(settingsHeight)
                    )
            )
            {
                _settingsScrollPosition = settingsScroll.scrollPosition;
                DrawInputSection();
                EditorGUILayout.Space();
                DrawOutputSection();
                EditorGUILayout.Space();
                DrawDiscoverySection();
            }

            // Splitter bar
            DrawSplitter();

            // Preview section (already has its own scroll view)
            using (new EditorGUILayout.VerticalScope(GUILayout.Height(previewHeight)))
            {
                DrawPreviewSection();
                EditorGUILayout.Space();
                DrawExtractionSection();
                EditorGUILayout.Space();
                DrawDangerZone();
            }

            _serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// Draws the horizontal splitter bar between settings and preview sections.
        /// </summary>
        private void DrawSplitter()
        {
            Rect splitterRect = GUILayoutUtility.GetRect(
                GUIContent.none,
                GUIStyle.none,
                GUILayout.Height(SplitterHeight),
                GUILayout.ExpandWidth(true)
            );

            // Draw splitter background
            EditorGUI.DrawRect(splitterRect, new Color(0.2f, 0.2f, 0.2f, 1f));

            // Draw grip lines in center
            float centerY = splitterRect.y + splitterRect.height * 0.5f;
            Rect gripRect = new Rect(splitterRect.center.x - 20f, centerY - 1f, 40f, 2f);
            EditorGUI.DrawRect(gripRect, new Color(0.5f, 0.5f, 0.5f, 1f));

            // Set cursor in interactive editor windows only.
            if (!EditorUi.Suppress)
            {
                EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeVertical);
            }
        }

        /// <summary>
        /// Handles mouse events for splitter dragging.
        /// </summary>
        private void HandleSplitterEvents()
        {
            Event e = Event.current;

            // Calculate splitter rect position (approximate, will be refined after layout)
            float splitterY = Mathf.Clamp(
                _splitterPosition,
                MinSettingsHeight,
                position.height - MinPreviewHeight - SplitterHeight
            );
            Rect splitterRect = new Rect(0f, splitterY, position.width, SplitterHeight);

            int controlId = GUIUtility.GetControlID(FocusType.Passive);

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && splitterRect.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = controlId;
                        _isDraggingSplitter = true;
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (_isDraggingSplitter && GUIUtility.hotControl == controlId)
                    {
                        _splitterPosition += e.delta.y;
                        _splitterPosition = Mathf.Clamp(
                            _splitterPosition,
                            MinSettingsHeight,
                            position.height - MinPreviewHeight - SplitterHeight
                        );
                        Repaint();
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (_isDraggingSplitter)
                    {
                        GUIUtility.hotControl = 0;
                        _isDraggingSplitter = false;
                        EditorPrefs.SetFloat(SplitterPositionPrefsKey, _splitterPosition);
                        e.Use();
                    }
                    break;
            }
        }

        private void DrawInputSection()
        {
            EditorGUILayout.LabelField("Input Directories", EditorStyles.boldLabel);
            PersistentDirectoryGUI.PathSelectorObjectArray(
                _inputDirectoriesProperty,
                nameof(SpriteSheetExtractor)
            );

            EditorGUILayout.PropertyField(
                _spriteNameRegexProperty,
                new GUIContent(
                    "Sprite Name Regex",
                    "Optional .NET regex to filter sprite sheets by file name (no extension)."
                )
            );

            ValidateRegex();
            if (!string.IsNullOrEmpty(_regexError))
            {
                EditorGUILayout.HelpBox($"Invalid regex: {_regexError}", MessageType.Error);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Extraction Mode", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(
                _extractionModeProperty,
                new GUIContent(
                    "Mode",
                    "FromMetadata: Use existing sprite metadata.\n"
                        + "GridBased: Slice texture by grid.\n"
                        + "AlphaDetection: Detect sprites by alpha boundaries.\n"
                        + "PaddedGrid: Grid with padding between cells."
                )
            );

            DrawExtractionModeOptions();
        }

        private void DrawExtractionModeOptions()
        {
            bool showGridOptions =
                _extractionMode == ExtractionMode.GridBased
                || _extractionMode == ExtractionMode.PaddedGrid;
            bool showPaddingOptions = _extractionMode == ExtractionMode.PaddedGrid;
            bool showAlphaOptions = _extractionMode == ExtractionMode.AlphaDetection;

            if (showGridOptions)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(
                        _gridSizeModeProperty,
                        new GUIContent(
                            "Grid Size Mode",
                            "Auto: Calculate grid from texture dimensions.\n"
                                + "Manual: Specify columns/rows or cell dimensions."
                        )
                    );
                    bool gridSizeModeChanged = EditorGUI.EndChangeCheck();

                    if (gridSizeModeChanged)
                    {
                        RegenerateEntriesUsingGlobalSettings();
                    }

                    if (_gridSizeMode == GridSizeMode.Manual)
                    {
                        EditorGUI.BeginChangeCheck();
                        EditorGUILayout.PropertyField(
                            _gridColumnsProperty,
                            new GUIContent("Columns", "Number of columns in the grid.")
                        );
                        EditorGUILayout.PropertyField(
                            _gridRowsProperty,
                            new GUIContent("Rows", "Number of rows in the grid.")
                        );
                        EditorGUILayout.PropertyField(
                            _cellWidthProperty,
                            new GUIContent(
                                "Cell Width",
                                "Width of each grid cell in pixels (0 = auto)."
                            )
                        );
                        EditorGUILayout.PropertyField(
                            _cellHeightProperty,
                            new GUIContent(
                                "Cell Height",
                                "Height of each grid cell in pixels (0 = auto)."
                            )
                        );
                        bool manualGridSettingsChanged = EditorGUI.EndChangeCheck();

                        _gridColumns = Mathf.Max(1, _gridColumns);
                        _gridRows = Mathf.Max(1, _gridRows);
                        _cellWidth = Mathf.Max(0, _cellWidth);
                        _cellHeight = Mathf.Max(0, _cellHeight);

                        if (manualGridSettingsChanged)
                        {
                            RegenerateEntriesUsingGlobalSettings();
                        }
                    }
                    else
                    {
                        DrawAutoDetectionAlgorithmUI();
                    }
                }
            }

            if (showPaddingOptions)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.LabelField("Padding", EditorStyles.miniBoldLabel);

                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(
                        _paddingLeftProperty,
                        new GUIContent("Left", "Padding from left edge of each cell.")
                    );
                    EditorGUILayout.PropertyField(
                        _paddingRightProperty,
                        new GUIContent("Right", "Padding from right edge of each cell.")
                    );
                    EditorGUILayout.PropertyField(
                        _paddingTopProperty,
                        new GUIContent("Top", "Padding from top edge of each cell.")
                    );
                    EditorGUILayout.PropertyField(
                        _paddingBottomProperty,
                        new GUIContent("Bottom", "Padding from bottom edge of each cell.")
                    );
                    bool paddingChanged = EditorGUI.EndChangeCheck();

                    _paddingLeft = Mathf.Max(0, _paddingLeft);
                    _paddingRight = Mathf.Max(0, _paddingRight);
                    _paddingTop = Mathf.Max(0, _paddingTop);
                    _paddingBottom = Mathf.Max(0, _paddingBottom);

                    if (paddingChanged)
                    {
                        RegenerateEntriesUsingGlobalSettings();
                    }
                }
            }

            if (showAlphaOptions)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(
                        _alphaThresholdProperty,
                        new GUIContent(
                            "Alpha Threshold",
                            "Pixels with alpha above this value are considered opaque. (0.0-1.0)"
                        )
                    );
                    bool alphaThresholdChanged = EditorGUI.EndChangeCheck();
                    _alphaThreshold = Mathf.Clamp01(_alphaThreshold);

                    if (alphaThresholdChanged)
                    {
                        RegenerateEntriesUsingGlobalSettings();
                    }
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Pivot Settings", EditorStyles.miniBoldLabel);

            EditorGUILayout.PropertyField(
                _pivotModeProperty,
                new GUIContent(
                    "Pivot Mode",
                    "Pivot point for extracted sprites. Custom allows specifying exact normalized coordinates."
                )
            );

            if (_pivotMode == PivotMode.Custom)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    // X slider
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("X", GUILayout.Width(20));
                        float newX = EditorGUILayout.Slider(_customPivot.x, 0f, 1f);
                        if (!Mathf.Approximately(newX, _customPivot.x))
                        {
                            _customPivot = new Vector2(newX, _customPivot.y);
                        }
                    }

                    // Y slider
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Y", GUILayout.Width(20));
                        float newY = EditorGUILayout.Slider(_customPivot.y, 0f, 1f);
                        if (!Mathf.Approximately(newY, _customPivot.y))
                        {
                            _customPivot = new Vector2(_customPivot.x, newY);
                        }
                    }

                    // Combined Vector2Field for direct input with clamping
                    EditorGUILayout.PropertyField(
                        _customPivotProperty,
                        new GUIContent(
                            "Custom Pivot",
                            "Custom pivot point in normalized coordinates (0-1). (0,0) is bottom-left, (1,1) is top-right."
                        )
                    );
                    _customPivot = new Vector2(
                        Mathf.Clamp01(_customPivot.x),
                        Mathf.Clamp01(_customPivot.y)
                    );
                }
            }
        }

        private void DrawAutoDetectionAlgorithmUI()
        {
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Auto-Detection Algorithm", EditorStyles.miniBoldLabel);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(
                _autoDetectionAlgorithmProperty,
                new GUIContent(
                    "Algorithm",
                    "Algorithm for automatic grid detection.\n"
                        + "AutoBest: Tries algorithms in order of speed, stops at 70% confidence.\n"
                        + "UniformGrid: Simple division (requires expected sprite count).\n"
                        + "BoundaryScoring: Scores grid lines by transparency.\n"
                        + "ClusterCentroid: Detects sprites, infers grid from spacing.\n"
                        + "DistanceTransform: Uses distance field peaks.\n"
                        + "RegionGrowing: Grows regions from local maxima."
                )
            );
            bool algorithmChanged = EditorGUI.EndChangeCheck();

            bool expectedCountChanged = false;
            bool isUniformGrid = _autoDetectionAlgorithm == AutoDetectionAlgorithm.UniformGrid;
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(
                _expectedSpriteCountHintProperty,
                new GUIContent(
                    isUniformGrid ? "Expected Sprite Count" : "Expected Sprite Count (Recommended)",
                    isUniformGrid
                        ? "Number of sprites in the sheet. Required for UniformGrid algorithm."
                        : "Number of sprites in the sheet. When set, algorithms use this to find the best grid that produces exactly this many cells. Highly recommended for accurate results."
                )
            );
            expectedCountChanged = EditorGUI.EndChangeCheck();
            _expectedSpriteCountHint = Mathf.Max(-1, _expectedSpriteCountHint);

            if (_expectedSpriteCountHint <= 0)
            {
                EditorGUILayout.HelpBox(
                    isUniformGrid
                        ? "UniformGrid requires a valid expected sprite count (> 0)."
                        : "Setting expected sprite count improves detection accuracy. The algorithm will find a grid that produces exactly this many cells.",
                    isUniformGrid ? MessageType.Warning : MessageType.Info
                );
            }

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(
                _snapToTextureDivisorProperty,
                new GUIContent(
                    "Snap to Divisor",
                    "When enabled, adjusts cell sizes to be exact divisors of texture dimensions, "
                        + "using transparency analysis to handle remainders intelligently."
                )
            );
            bool snapChanged = EditorGUI.EndChangeCheck();

            if (algorithmChanged || expectedCountChanged || snapChanged)
            {
                if (DiagnosticsEnabled)
                {
                    this.Log(
                        $"DrawAutoDetectionAlgorithmUI: settings changed (algorithmChanged={algorithmChanged}, expectedCountChanged={expectedCountChanged}, snapChanged={snapChanged}), calling RegenerateEntriesUsingGlobalSettings"
                    );
                }
                RegenerateEntriesUsingGlobalSettings();
            }
        }

        private void ValidateRegex()
        {
            if (string.Equals(_spriteNameRegex, _lastValidatedRegex, StringComparison.Ordinal))
            {
                return;
            }

            _lastValidatedRegex = _spriteNameRegex;
            if (string.IsNullOrWhiteSpace(_spriteNameRegex))
            {
                _regexError = null;
                return;
            }

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

        private void DrawOutputSection()
        {
            EditorGUILayout.LabelField("Output Settings", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(
                _outputDirectoryProperty,
                new GUIContent(
                    "Output Directory",
                    "Directory where extracted sprites will be saved. Required."
                )
            );

            EditorGUILayout.PropertyField(
                _namingPrefixProperty,
                new GUIContent(
                    "Naming Prefix Override",
                    "Optional prefix for output filenames. If empty, uses original texture name."
                )
            );

            EditorGUILayout.PropertyField(
                _preserveImportSettingsProperty,
                new GUIContent(
                    "Preserve Import Settings",
                    "Copy PPU, pivot, borders, and other settings from original sprite sheet."
                )
            );

            EditorGUILayout.PropertyField(
                _overwriteExistingProperty,
                new GUIContent(
                    "Overwrite Existing Files",
                    "If enabled, existing files in output directory will be overwritten."
                )
            );
            if (_overwriteExisting)
            {
                EditorGUILayout.HelpBox(
                    "Warning: Existing files will be overwritten without confirmation.",
                    MessageType.Warning
                );
            }

            EditorGUILayout.PropertyField(
                _dryRunProperty,
                new GUIContent(
                    "Dry Run",
                    "Simulate extraction without writing files. Shows what would be created."
                )
            );
        }

        private void DrawDiscoverySection()
        {
            EditorGUILayout.LabelField("Discovery", EditorStyles.boldLabel);

            if (GUILayout.Button("Find Sprite Sheets"))
            {
                if (!string.IsNullOrEmpty(_regexError))
                {
                    ShowNotification(new GUIContent("Invalid regex. Fix it before searching."));
                    return;
                }

                DiscoverSpriteSheets();
            }
        }

        private void DrawPreviewSection()
        {
            if (_discoveredSheets == null || _discoveredSheets.Count == 0)
            {
                if (_discoveredSheets != null)
                {
                    EditorGUILayout.LabelField(
                        "No sprite sheets found in selected directories.",
                        EditorStyles.label
                    );
                }
                return;
            }

            EditorGUILayout.LabelField(
                $"Found {_discoveredSheets.Count} sprite sheet(s)",
                EditorStyles.boldLabel
            );

            DrawBulkActions();

            EditorGUILayout.PropertyField(
                _sortModeProperty,
                new GUIContent("Sort Mode", "How to sort sprites within each sheet for extraction.")
            );

            EditorGUILayout.PropertyField(
                _previewSizeModeProperty,
                new GUIContent(
                    "Preview Size",
                    "Size of sprite preview thumbnails. RealSize uses actual sprite dimensions."
                )
            );

            // Use SerializedProperty values for comparison because backing fields aren't updated
            // until ApplyModifiedProperties() is called at the end of OnGUI
            PreviewSizeMode currentPreviewSizeMode = (PreviewSizeMode)
                _previewSizeModeProperty.enumValueIndex;
            ExtractionMode currentExtractionMode = (ExtractionMode)
                _extractionModeProperty.enumValueIndex;
            bool previewSizeModeChanged = _lastPreviewSizeMode != currentPreviewSizeMode;
            bool extractionModeChanged = _lastExtractionMode != currentExtractionMode;

            if (previewSizeModeChanged && !extractionModeChanged && !_previewRegenerationScheduled)
            {
                _lastPreviewSizeMode = currentPreviewSizeMode;
                _previewRegenerationScheduled = true;
                EditorApplication.delayCall += RegeneratePreviewTexturesOnly;
            }
            else if (extractionModeChanged && !_previewRegenerationScheduled)
            {
                _lastPreviewSizeMode = currentPreviewSizeMode;
                _lastExtractionMode = currentExtractionMode;
                _previewRegenerationScheduled = true;
                EditorApplication.delayCall += RegenerateAllPreviewTextures;
            }

            EditorGUILayout.PropertyField(
                _showOverlayProperty,
                new GUIContent(
                    "Show Overlay",
                    "Default setting for displaying sprite bounds outline on source texture previews. Can be overridden per-sheet."
                )
            );
            // Use _showOverlayProperty.boolValue for comparison because _showOverlay isn't updated
            // until ApplyModifiedProperties() is called at the end of OnGUI
            if (_lastShowOverlay != _showOverlayProperty.boolValue)
            {
                _lastShowOverlay = _showOverlayProperty.boolValue;
                Repaint();
            }
            if (_showOverlayProperty.boolValue)
            {
                EditorGUILayout.PropertyField(
                    _overlayColorProperty,
                    new GUIContent("Overlay Color", "Color of the overlay lines.")
                );
            }

            EditorGUILayout.PropertyField(
                _pivotMarkerColorProperty,
                new GUIContent("Pivot Marker Color", "Color for pivot position crosshairs.")
            );

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            try
            {
                for (int i = 0; i < _discoveredSheets.Count; ++i)
                {
                    DrawSpriteSheetEntry(_discoveredSheets[i]);
                }
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawBulkActions()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select All"))
                {
                    SetAllSelection(true);
                }
                if (GUILayout.Button("Select None"))
                {
                    SetAllSelection(false);
                }
                if (GUILayout.Button("Expand All"))
                {
                    SetAllExpanded(true);
                }
                if (GUILayout.Button("Collapse All"))
                {
                    SetAllExpanded(false);
                }
                if (GUILayout.Button("Apply Global to All"))
                {
                    ApplyGlobalSettingsToAll();
                }
            }
        }

        internal void ApplyGlobalSettingsToAll()
        {
            if (_discoveredSheets == null)
            {
                return;
            }

            for (int i = 0; i < _discoveredSheets.Count; ++i)
            {
                SpriteSheetEntry entry = _discoveredSheets[i];
                entry._useGlobalSettings = false;
                entry._extractionModeOverride = _extractionMode;
                entry._gridSizeModeOverride = _gridSizeMode;
                entry._gridColumnsOverride = _gridColumns;
                entry._gridRowsOverride = _gridRows;
                entry._cellWidthOverride = _cellWidth;
                entry._cellHeightOverride = _cellHeight;
                entry._paddingLeftOverride = _paddingLeft;
                entry._paddingRightOverride = _paddingRight;
                entry._paddingTopOverride = _paddingTop;
                entry._paddingBottomOverride = _paddingBottom;
                entry._alphaThresholdOverride = _alphaThreshold;
                entry._showOverlayOverride = _showOverlay;
                entry._usePivotMarkerColorOverride = false;
                entry._pivotMarkerColorOverride = _pivotMarkerColor;
            }

            Repaint();
        }

        private void SetAllSelection(bool selected)
        {
            if (_discoveredSheets == null)
            {
                return;
            }

            for (int i = 0; i < _discoveredSheets.Count; ++i)
            {
                SpriteSheetEntry entry = _discoveredSheets[i];
                entry._isSelected = selected;
                if (entry._sprites != null)
                {
                    for (int j = 0; j < entry._sprites.Count; ++j)
                    {
                        entry._sprites[j]._isSelected = selected;
                    }
                }
            }
            Repaint();
        }

        private void SetAllExpanded(bool expanded)
        {
            if (_discoveredSheets == null)
            {
                return;
            }

            for (int i = 0; i < _discoveredSheets.Count; ++i)
            {
                _discoveredSheets[i]._isExpanded = expanded;
            }
            Repaint();
        }

        /// <summary>
        /// Selects all sprites in the specified entry.
        /// </summary>
        internal void SelectAll(SpriteSheetEntry entry)
        {
            if (entry?._sprites == null)
            {
                return;
            }

            for (int i = 0; i < entry._sprites.Count; ++i)
            {
                entry._sprites[i]._isSelected = true;
            }
        }

        /// <summary>
        /// Deselects all sprites in the specified entry.
        /// </summary>
        internal void SelectNone(SpriteSheetEntry entry)
        {
            if (entry?._sprites == null)
            {
                return;
            }

            for (int i = 0; i < entry._sprites.Count; ++i)
            {
                entry._sprites[i]._isSelected = false;
            }
        }

        private void DrawSpriteSheetEntry(SpriteSheetEntry entry)
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                int spriteCount = entry._sprites != null ? entry._sprites.Count : 0;
                bool isStale = IsEntryStale(entry);

                using (new EditorGUILayout.HorizontalScope())
                {
                    entry._isSelected = EditorGUILayout.Toggle(
                        entry._isSelected,
                        GUILayout.Width(20)
                    );

                    string entryLabel = isStale
                        ? $"{Path.GetFileName(entry._assetPath)} ({spriteCount} sprites) (stale)"
                        : $"{Path.GetFileName(entry._assetPath)} ({spriteCount} sprites)";

                    entry._isExpanded = EditorGUILayout.Foldout(
                        entry._isExpanded,
                        entryLabel,
                        true
                    );

                    if (entry._importMode == SpriteImportMode.Single)
                    {
                        GUIContent warningIcon = EditorGUIUtility.IconContent(
                            "console.warnicon.sml"
                        );
                        warningIcon.tooltip =
                            "Single sprite mode - only one sprite will be extracted.";
                        GUILayout.Label(warningIcon, GUILayout.Width(20));
                    }
                }

                if (entry._isExpanded)
                {
                    DrawPerSheetSettings(entry);
                }

                DrawGridValidationWarnings(entry);

                if (entry._isExpanded)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        GUILayout.Space(EditorGUI.indentLevel * 15f);
                        if (GUILayout.Button("Preview Slicing", GUILayout.Width(120)))
                        {
                            entry._sourcePreviewExpanded = true;
                            Repaint();
                        }
                    }
                }

                if (entry._isExpanded && entry._sprites != null)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        DrawSourceTexturePreview(entry);

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button("Select All", GUILayout.Width(80)))
                            {
                                for (int i = 0; i < entry._sprites.Count; ++i)
                                {
                                    entry._sprites[i]._isSelected = true;
                                }
                            }
                            if (GUILayout.Button("Select None", GUILayout.Width(80)))
                            {
                                for (int i = 0; i < entry._sprites.Count; ++i)
                                {
                                    entry._sprites[i]._isSelected = false;
                                }
                            }

                            GUILayout.FlexibleSpace();

                            if (
                                GUILayout.Button(
                                    new GUIContent(
                                        "Enable All Pivots",
                                        "Enable pivot override for all sprites, copying current effective pivot as starting value"
                                    ),
                                    GUILayout.Width(110)
                                )
                            )
                            {
                                PivotMode effectiveMode = GetEffectivePivotMode(entry);
                                Vector2 effectivePivot = GetEffectiveCustomPivot(entry);

                                for (int i = 0; i < entry._sprites.Count; ++i)
                                {
                                    SpriteEntryData sprite = entry._sprites[i];
                                    if (!sprite._usePivotOverride)
                                    {
                                        sprite._usePivotOverride = true;
                                        sprite._pivotModeOverride = effectiveMode;
                                        sprite._customPivotOverride = effectivePivot;
                                    }
                                }
                                Repaint();
                            }

                            if (
                                GUILayout.Button(
                                    new GUIContent(
                                        "Disable All Pivots",
                                        "Disable pivot override for all sprites (reverts to sheet/global pivot)"
                                    ),
                                    GUILayout.Width(115)
                                )
                            )
                            {
                                for (int i = 0; i < entry._sprites.Count; ++i)
                                {
                                    entry._sprites[i]._usePivotOverride = false;
                                }
                                Repaint();
                            }
                        }

                        List<SpriteEntryData> sortedSprites = GetSortedSprites(entry._sprites);
                        for (int i = 0; i < sortedSprites.Count; ++i)
                        {
                            SpriteEntryData sprite = sortedSprites[i];
                            DrawSpriteEntry(entry, sprite, i);
                        }
                    }
                }
            }
        }

        private void DrawGridValidationWarnings(SpriteSheetEntry entry)
        {
            ExtractionMode effectiveExtractionMode = GetEffectiveExtractionMode(entry);
            if (
                effectiveExtractionMode != ExtractionMode.GridBased
                && effectiveExtractionMode != ExtractionMode.PaddedGrid
            )
            {
                return;
            }

            if (entry._texture == null)
            {
                return;
            }

            int textureWidth = entry._texture.width;
            int textureHeight = entry._texture.height;

            int effectiveCellWidth;
            int effectiveCellHeight;
            int effectiveColumns;
            int effectiveRows;

            CalculateGridDimensions(
                textureWidth,
                textureHeight,
                entry,
                out effectiveColumns,
                out effectiveRows,
                out effectiveCellWidth,
                out effectiveCellHeight
            );

            bool widthMismatch = (effectiveCellWidth * effectiveColumns) != textureWidth;
            bool heightMismatch = (effectiveCellHeight * effectiveRows) != textureHeight;

            if (widthMismatch || heightMismatch)
            {
                string warning = "Grid dimensions do not evenly divide texture:";
                if (widthMismatch)
                {
                    warning +=
                        $" Width {textureWidth} / {effectiveColumns} cols = {(float)textureWidth / effectiveColumns:F2} pixels/cell.";
                }
                if (heightMismatch)
                {
                    warning +=
                        $" Height {textureHeight} / {effectiveRows} rows = {(float)textureHeight / effectiveRows:F2} pixels/cell.";
                }
                EditorGUILayout.HelpBox(warning, MessageType.Warning);
            }
        }

        private void DrawPerSheetSettings(SpriteSheetEntry entry)
        {
            using (new EditorGUI.IndentLevelScope())
            {
                entry._perSheetSettingsFoldout = EditorGUILayout.Foldout(
                    entry._perSheetSettingsFoldout,
                    "Per-Sheet Settings",
                    true
                );

                if (!entry._perSheetSettingsFoldout)
                {
                    return;
                }

                using (new EditorGUI.IndentLevelScope())
                {
                    bool previousUseGlobal = entry._useGlobalSettings;
                    entry._useGlobalSettings = EditorGUILayout.Toggle(
                        new GUIContent(
                            "Use Global Settings",
                            "When enabled, this sheet uses the global extraction settings."
                        ),
                        entry._useGlobalSettings
                    );

                    // When transitioning from global to per-sheet settings,
                    // initialize overrides from current effective values to prevent UI desync
                    // and regenerate sprites to clear stale data from the previous mode
                    bool regeneratedForGlobalToPerSheet = false;
                    if (previousUseGlobal && !entry._useGlobalSettings)
                    {
                        InitializeOverridesFromGlobal(entry);
                        // Use SchedulePreviewRegenerationForEntry to ensure overlay updates
                        SchedulePreviewRegenerationForEntry(entry);
                        regeneratedForGlobalToPerSheet = true;
                    }

                    if (!entry._useGlobalSettings)
                    {
                        DrawPerSheetOverrideFields(entry);

                        DrawCopySettingsFromButton(entry);
                    }

                    DrawConfigButtons(entry);

                    // Schedule preview regeneration when toggling between global and per-sheet settings,
                    // but skip if we already regenerated during global-to-per-sheet transition above
                    if (
                        previousUseGlobal != entry._useGlobalSettings
                        && !regeneratedForGlobalToPerSheet
                    )
                    {
                        SchedulePreviewRegenerationForEntry(entry);
                    }
                }
            }
        }

        private void DrawConfigButtons(SpriteSheetEntry entry)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Configuration", EditorStyles.miniBoldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save Config", GUILayout.Width(100)))
                {
                    _ = SaveConfig(entry);
                }

                if (GUILayout.Button("Load Config", GUILayout.Width(100)))
                {
                    _ = LoadConfig(entry);
                    Repaint();
                }

                if (entry._configLoaded)
                {
                    GUIStyle badgeStyle = new(EditorStyles.miniLabel);

                    if (entry._configStale)
                    {
                        badgeStyle.normal.textColor = new Color(0.8f, 0.6f, 0f);
                        EditorGUILayout.LabelField("Config Stale", badgeStyle, GUILayout.Width(80));
                    }
                    else
                    {
                        badgeStyle.normal.textColor = new Color(0f, 0.7f, 0f);
                        EditorGUILayout.LabelField(
                            "Config Loaded",
                            badgeStyle,
                            GUILayout.Width(80)
                        );
                    }
                }
            }
        }

        /// <summary>
        /// Draws per-sheet override fields for extraction settings when global settings are disabled.
        /// Allows configuration of extraction mode, grid options, padding, alpha threshold, and pivot.
        /// Regenerates sprites immediately when extraction mode changes to clear stale outlines.
        /// </summary>
        /// <param name="entry">The sprite sheet entry to draw override fields for.</param>
        private void DrawPerSheetOverrideFields(SpriteSheetEntry entry)
        {
            ExtractionMode previousExtractionMode =
                entry._extractionModeOverride ?? _extractionMode;
            entry._extractionModeOverride = (ExtractionMode)
                EditorGUILayout.EnumPopup(
                    new GUIContent("Extraction Mode", "How sprites are extracted from this sheet."),
                    entry._extractionModeOverride ?? _extractionMode
                );

            ExtractionMode effectiveMode = entry._extractionModeOverride.Value;
            bool showGridOptions =
                effectiveMode == ExtractionMode.GridBased
                || effectiveMode == ExtractionMode.PaddedGrid;
            bool showPaddingOptions = effectiveMode == ExtractionMode.PaddedGrid;
            bool showAlphaOptions = effectiveMode == ExtractionMode.AlphaDetection;

            if (showGridOptions)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    GridSizeMode previousGridSizeMode =
                        entry._gridSizeModeOverride ?? _gridSizeMode;
                    entry._gridSizeModeOverride = (GridSizeMode)
                        EditorGUILayout.EnumPopup(
                            new GUIContent(
                                "Grid Size Mode",
                                "Auto: Calculate grid from texture dimensions.\nManual: Specify columns/rows or cell dimensions."
                            ),
                            entry._gridSizeModeOverride ?? _gridSizeMode
                        );

                    if (entry._gridSizeModeOverride.Value != previousGridSizeMode)
                    {
                        entry._cachedAlgorithmResult = null;
                        // Use SchedulePreviewRegenerationForEntry to ensure overlay updates
                        SchedulePreviewRegenerationForEntry(entry);
                    }

                    GridSizeMode effectiveGridMode = entry._gridSizeModeOverride.Value;
                    if (effectiveGridMode == GridSizeMode.Manual)
                    {
                        int previousColumns = entry._gridColumnsOverride ?? _gridColumns;
                        int previousRows = entry._gridRowsOverride ?? _gridRows;
                        int previousCellWidth = entry._cellWidthOverride ?? _cellWidth;
                        int previousCellHeight = entry._cellHeightOverride ?? _cellHeight;

                        int gridColumnsValue = entry._gridColumnsOverride ?? _gridColumns;
                        gridColumnsValue = EditorGUILayout.IntField(
                            new GUIContent("Columns", "Number of columns in the grid."),
                            gridColumnsValue
                        );
                        entry._gridColumnsOverride = Mathf.Max(1, gridColumnsValue);

                        int gridRowsValue = entry._gridRowsOverride ?? _gridRows;
                        gridRowsValue = EditorGUILayout.IntField(
                            new GUIContent("Rows", "Number of rows in the grid."),
                            gridRowsValue
                        );
                        entry._gridRowsOverride = Mathf.Max(1, gridRowsValue);

                        int cellWidthValue = entry._cellWidthOverride ?? _cellWidth;
                        cellWidthValue = EditorGUILayout.IntField(
                            new GUIContent(
                                "Cell Width",
                                "Width of each grid cell in pixels (0 = auto)."
                            ),
                            cellWidthValue
                        );
                        entry._cellWidthOverride = Mathf.Max(0, cellWidthValue);

                        int cellHeightValue = entry._cellHeightOverride ?? _cellHeight;
                        cellHeightValue = EditorGUILayout.IntField(
                            new GUIContent(
                                "Cell Height",
                                "Height of each grid cell in pixels (0 = auto)."
                            ),
                            cellHeightValue
                        );
                        entry._cellHeightOverride = Mathf.Max(0, cellHeightValue);

                        bool manualGridSettingsChanged =
                            entry._gridColumnsOverride != previousColumns
                            || entry._gridRowsOverride != previousRows
                            || entry._cellWidthOverride != previousCellWidth
                            || entry._cellHeightOverride != previousCellHeight;

                        if (manualGridSettingsChanged)
                        {
                            entry._cachedAlgorithmResult = null;
                            // Use SchedulePreviewRegenerationForEntry to ensure overlay updates
                            SchedulePreviewRegenerationForEntry(entry);
                        }
                    }
                    else if (effectiveGridMode == GridSizeMode.Auto)
                    {
                        AutoDetectionAlgorithm currentAlgorithm =
                            entry._autoDetectionAlgorithmOverride ?? _autoDetectionAlgorithm;
                        AutoDetectionAlgorithm newAlgorithm = (AutoDetectionAlgorithm)
                            EditorGUILayout.EnumPopup(
                                new GUIContent(
                                    "Algorithm",
                                    "Algorithm for automatic grid detection.\n"
                                        + "AutoBest: Tries algorithms in order of speed, stops at 70% confidence.\n"
                                        + "UniformGrid: Simple division (requires expected sprite count).\n"
                                        + "BoundaryScoring: Scores grid lines by transparency.\n"
                                        + "ClusterCentroid: Detects sprites, infers grid from spacing.\n"
                                        + "DistanceTransform: Uses distance field peaks.\n"
                                        + "RegionGrowing: Grows regions from local maxima."
                                ),
                                currentAlgorithm
                            );
                        if (newAlgorithm != currentAlgorithm)
                        {
                            entry._autoDetectionAlgorithmOverride = newAlgorithm;
                            entry._cachedAlgorithmResult = null;
                            // Use SchedulePreviewRegenerationForEntry to preserve preview textures
                            // and ensure overlay updates properly when algorithm changes
                            SchedulePreviewRegenerationForEntry(entry);
                        }

                        {
                            bool entryIsUniformGrid =
                                newAlgorithm == AutoDetectionAlgorithm.UniformGrid;
                            int currentExpectedCount =
                                entry._expectedSpriteCountOverride ?? _expectedSpriteCountHint;
                            int newExpectedCount = EditorGUILayout.IntField(
                                new GUIContent(
                                    entryIsUniformGrid
                                        ? "Expected Sprite Count"
                                        : "Expected Sprite Count (Recommended)",
                                    entryIsUniformGrid
                                        ? "Number of sprites in the sheet. Required for UniformGrid algorithm."
                                        : "Number of sprites in the sheet. When set, algorithms use this to find the best grid that produces exactly this many cells."
                                ),
                                currentExpectedCount
                            );
                            newExpectedCount = Mathf.Max(-1, newExpectedCount);
                            if (newExpectedCount != currentExpectedCount)
                            {
                                entry._expectedSpriteCountOverride = newExpectedCount;
                                entry._cachedAlgorithmResult = null;
                                // Use SchedulePreviewRegenerationForEntry to ensure overlay updates
                                SchedulePreviewRegenerationForEntry(entry);
                            }

                            if (newExpectedCount <= 0 && entryIsUniformGrid)
                            {
                                EditorGUILayout.HelpBox(
                                    "UniformGrid requires a valid expected sprite count (> 0).",
                                    MessageType.Warning
                                );
                            }
                        }

                        bool currentSnapValue =
                            entry._snapToTextureDivisorOverride ?? _snapToTextureDivisor;
                        bool newSnapValue = EditorGUILayout.Toggle(
                            new GUIContent(
                                "Snap to Divisor",
                                "When enabled, adjusts cell sizes to be exact divisors of texture dimensions."
                            ),
                            currentSnapValue
                        );
                        if (newSnapValue != currentSnapValue)
                        {
                            entry._snapToTextureDivisorOverride = newSnapValue;
                            entry._cachedAlgorithmResult = null;
                            // Use SchedulePreviewRegenerationForEntry to ensure overlay updates
                            SchedulePreviewRegenerationForEntry(entry);
                        }
                    }
                }
            }

            if (showPaddingOptions)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUILayout.LabelField("Padding", EditorStyles.miniBoldLabel);

                    int previousPaddingLeft = entry._paddingLeftOverride ?? _paddingLeft;
                    int paddingLeftValue = EditorGUILayout.IntField(
                        new GUIContent("Left", "Padding from left edge of each cell."),
                        previousPaddingLeft
                    );
                    entry._paddingLeftOverride = Mathf.Max(0, paddingLeftValue);

                    int previousPaddingRight = entry._paddingRightOverride ?? _paddingRight;
                    int paddingRightValue = EditorGUILayout.IntField(
                        new GUIContent("Right", "Padding from right edge of each cell."),
                        previousPaddingRight
                    );
                    entry._paddingRightOverride = Mathf.Max(0, paddingRightValue);

                    int previousPaddingTop = entry._paddingTopOverride ?? _paddingTop;
                    int paddingTopValue = EditorGUILayout.IntField(
                        new GUIContent("Top", "Padding from top edge of each cell."),
                        previousPaddingTop
                    );
                    entry._paddingTopOverride = Mathf.Max(0, paddingTopValue);

                    int previousPaddingBottom = entry._paddingBottomOverride ?? _paddingBottom;
                    int paddingBottomValue = EditorGUILayout.IntField(
                        new GUIContent("Bottom", "Padding from bottom edge of each cell."),
                        previousPaddingBottom
                    );
                    entry._paddingBottomOverride = Mathf.Max(0, paddingBottomValue);

                    bool paddingChanged =
                        previousPaddingLeft != entry._paddingLeftOverride
                        || previousPaddingRight != entry._paddingRightOverride
                        || previousPaddingTop != entry._paddingTopOverride
                        || previousPaddingBottom != entry._paddingBottomOverride;
                    if (paddingChanged)
                    {
                        entry._cachedAlgorithmResult = null;
                        // Use SchedulePreviewRegenerationForEntry to ensure overlay updates
                        SchedulePreviewRegenerationForEntry(entry);
                    }
                }
            }

            if (showAlphaOptions)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    float currentAlphaThreshold = entry._alphaThresholdOverride ?? _alphaThreshold;
                    float newAlphaThreshold = EditorGUILayout.Slider(
                        new GUIContent(
                            "Alpha Threshold",
                            "Pixels with alpha above this value are considered opaque. (0.0-1.0)"
                        ),
                        currentAlphaThreshold,
                        0f,
                        1f
                    );
                    if (!Mathf.Approximately(newAlphaThreshold, currentAlphaThreshold))
                    {
                        entry._alphaThresholdOverride = newAlphaThreshold;
                        entry._cachedAlgorithmResult = null;
                        // Use SchedulePreviewRegenerationForEntry to ensure overlay updates
                        SchedulePreviewRegenerationForEntry(entry);
                    }
                }
            }

            // Show Overlay toggle is available for ALL extraction modes, not just grid-based
            // This allows users to see sprite bounds outlines regardless of how sprites are extracted
            {
                bool currentOverlayValue = entry._showOverlayOverride ?? _showOverlay;
                bool newOverlayValue = EditorGUILayout.Toggle(
                    new GUIContent(
                        "Show Overlay",
                        "Display sprite bounds outline on the source texture preview for this specific sheet. Overrides the global setting."
                    ),
                    currentOverlayValue
                );
                if (currentOverlayValue != newOverlayValue)
                {
                    entry._showOverlayOverride = newOverlayValue;
                    Repaint();
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Pivot Settings", EditorStyles.miniBoldLabel);

            PivotMode currentPivotMode = entry._pivotModeOverride ?? _pivotMode;
            PivotMode newPivotMode = (PivotMode)
                EditorGUILayout.EnumPopup(
                    new GUIContent(
                        "Pivot Mode",
                        "Pivot point for sprites from this sheet. Custom allows specifying exact coordinates."
                    ),
                    currentPivotMode
                );
            entry._pivotModeOverride = newPivotMode;

            if (newPivotMode == PivotMode.Custom)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    Vector2 currentCustomPivot = entry._customPivotOverride ?? _customPivot;

                    // X slider
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("X", GUILayout.Width(20));
                        float newX = EditorGUILayout.Slider(currentCustomPivot.x, 0f, 1f);
                        if (!Mathf.Approximately(newX, currentCustomPivot.x))
                        {
                            currentCustomPivot = new Vector2(newX, currentCustomPivot.y);
                        }
                    }

                    // Y slider
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("Y", GUILayout.Width(20));
                        float newY = EditorGUILayout.Slider(currentCustomPivot.y, 0f, 1f);
                        if (!Mathf.Approximately(newY, currentCustomPivot.y))
                        {
                            currentCustomPivot = new Vector2(currentCustomPivot.x, newY);
                        }
                    }

                    // Combined Vector2Field for direct input with clamping
                    Vector2 newCustomPivot = EditorGUILayout.Vector2Field(
                        new GUIContent(
                            "Custom Pivot",
                            "Custom pivot in normalized coordinates (0-1). (0,0) is bottom-left, (1,1) is top-right."
                        ),
                        currentCustomPivot
                    );
                    entry._customPivotOverride = new Vector2(
                        Mathf.Clamp01(newCustomPivot.x),
                        Mathf.Clamp01(newCustomPivot.y)
                    );
                }
            }

            entry._usePivotMarkerColorOverride = EditorGUILayout.Toggle(
                new GUIContent(
                    "Override Pivot Color",
                    "Override the global pivot marker color for this sheet."
                ),
                entry._usePivotMarkerColorOverride
            );
            if (entry._usePivotMarkerColorOverride)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    entry._pivotMarkerColorOverride = EditorGUILayout.ColorField(
                        new GUIContent(
                            "Pivot Marker Color",
                            "Color for pivot position crosshairs on this sheet."
                        ),
                        entry._pivotMarkerColorOverride
                    );
                }
            }

            // When extraction mode changes, regenerate sprites to clear stale outlines
            // and ensure the preview reflects the new extraction mode settings
            if (previousExtractionMode != entry._extractionModeOverride.Value)
            {
                entry._cachedAlgorithmResult = null;
                // Use SchedulePreviewRegenerationForEntry to ensure overlay updates properly
                SchedulePreviewRegenerationForEntry(entry);
            }
        }

        private void DrawCopySettingsFromButton(SpriteSheetEntry entry)
        {
            if (_discoveredSheets == null || _discoveredSheets.Count < 2)
            {
                return;
            }

            if (GUILayout.Button("Copy Settings From..."))
            {
                GenericMenu menu = new GenericMenu();

                for (int i = 0; i < _discoveredSheets.Count; ++i)
                {
                    SpriteSheetEntry sourceEntry = _discoveredSheets[i];
                    if (sourceEntry == entry)
                    {
                        continue;
                    }

                    string entryName = Path.GetFileName(sourceEntry._assetPath);
                    int capturedIndex = i;
                    menu.AddItem(
                        new GUIContent(entryName),
                        false,
                        () => CopySettingsFromEntry(_discoveredSheets[capturedIndex], entry)
                    );
                }

                menu.ShowAsContext();
            }
        }

        /// <summary>
        /// Copies all extraction settings from a source sprite sheet entry to a target entry.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method transfers all per-entry configuration overrides including extraction mode,
        /// grid dimensions, padding, alpha threshold, pivot settings, and auto-detection algorithm.
        /// After copying settings, it automatically schedules preview regeneration for the target entry.
        /// </para>
        /// <para>
        /// Use this method to quickly replicate configuration from one sprite sheet to another,
        /// enabling consistent extraction settings across multiple sheets.
        /// </para>
        /// </remarks>
        /// <param name="source">The sprite sheet entry to copy settings from. If null, the method returns immediately.</param>
        /// <param name="target">The sprite sheet entry to apply settings to. If null, the method returns immediately.</param>
        internal void CopySettingsFromEntry(SpriteSheetEntry source, SpriteSheetEntry target)
        {
            if (source == null || target == null)
            {
                return;
            }

            target._useGlobalSettings = source._useGlobalSettings;
            target._extractionModeOverride = source._extractionModeOverride;
            target._gridSizeModeOverride = source._gridSizeModeOverride;
            target._gridColumnsOverride = source._gridColumnsOverride;
            target._gridRowsOverride = source._gridRowsOverride;
            target._cellWidthOverride = source._cellWidthOverride;
            target._cellHeightOverride = source._cellHeightOverride;
            target._paddingLeftOverride = source._paddingLeftOverride;
            target._paddingRightOverride = source._paddingRightOverride;
            target._paddingTopOverride = source._paddingTopOverride;
            target._paddingBottomOverride = source._paddingBottomOverride;
            target._alphaThresholdOverride = source._alphaThresholdOverride;
            target._showOverlayOverride = source._showOverlayOverride;
            target._pivotModeOverride = source._pivotModeOverride;
            target._customPivotOverride = source._customPivotOverride;
            target._autoDetectionAlgorithmOverride = source._autoDetectionAlgorithmOverride;
            target._expectedSpriteCountOverride = source._expectedSpriteCountOverride;
            target._snapToTextureDivisorOverride = source._snapToTextureDivisorOverride;
            target._usePivotMarkerColorOverride = source._usePivotMarkerColorOverride;
            target._pivotMarkerColorOverride = source._pivotMarkerColorOverride;

            SchedulePreviewRegenerationForEntry(target);
        }

        /// <summary>
        /// Schedules preview regeneration for a sprite sheet entry while preserving existing preview textures.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method performs a careful sprite list repopulation that maintains visual continuity during
        /// Unity Editor repaints. Preview textures from the old sprite list are transferred to matching
        /// sprites in the new list based on their rects, preventing "grey question mark" artifacts that
        /// would otherwise appear if Unity repaints during AssetDatabase operations.
        /// </para>
        /// <para>
        /// The method follows this sequence:
        /// <list type="number">
        /// <item><description>Stores preview textures from existing sprites, keyed by rect</description></item>
        /// <item><description>Refreshes the texture and importer references if needed</description></item>
        /// <item><description>Repopulates sprites into a new list without modifying the original</description></item>
        /// <item><description>Transfers matching previews to the new sprites</description></item>
        /// <item><description>Atomically swaps the sprite lists to minimize visual disruption</description></item>
        /// <item><description>Generates new previews for sprites that need them</description></item>
        /// <item><description>Cleans up orphaned textures to prevent memory leaks</description></item>
        /// </list>
        /// </para>
        /// </remarks>
        /// <param name="entry">The sprite sheet entry to regenerate previews for. If null, the method returns immediately.</param>
        private void SchedulePreviewRegenerationForEntry(SpriteSheetEntry entry)
        {
            if (entry == null)
            {
                if (DiagnosticsEnabled)
                {
                    this.Log(
                        $"SchedulePreviewRegenerationForEntry: entry is null, returning early"
                    );
                }
                return;
            }

            if (DiagnosticsEnabled)
            {
                this.Log(
                    $"SchedulePreviewRegenerationForEntry: START for '{entry._assetPath}', setting _regenerationInProgress=true"
                );
            }
            _regenerationInProgress = true;
            // Store preview textures from current sprites, keyed by rect for transfer
            using PooledResource<Dictionary<Rect, Texture2D>> rectToPreviewLease = DictionaryBuffer<
                Rect,
                Texture2D
            >.Dictionary.Get(out Dictionary<Rect, Texture2D> rectToPreview);
            try
            {
                // Keep the old sprites list reference - it will remain visible during repopulation
                // to prevent grey question marks if Unity repaints during AssetDatabase operations
                List<SpriteEntryData> oldSprites = entry._sprites;

                // Store preview textures from old sprites, keyed by rect for transfer
                // IMPORTANT: Do NOT clear _previewTexture from old sprites yet - they need to remain
                // visible if Unity repaints during population
                if (oldSprites != null)
                {
                    for (int i = 0; i < oldSprites.Count; ++i)
                    {
                        SpriteEntryData sprite = oldSprites[i];
                        if (sprite != null && sprite._previewTexture != null)
                        {
                            // If duplicate rect exists, destroy the old texture to prevent memory leak
                            if (
                                rectToPreview.TryGetValue(sprite._rect, out Texture2D existing)
                                && existing != null
                            )
                            {
                                DestroyImmediate(existing);
                            }
                            // Use rect as key to identify matching sprites after repopulation
                            // Keep the preview in the old sprite for now - it will be displayed if repaint occurs
                            rectToPreview[sprite._rect] = sprite._previewTexture;
                        }
                    }
                }

                // Ensure texture and importer are fresh before repopulation
                // This handles cases where the asset may have been reimported or modified
                if (entry._texture == null || !entry._texture)
                {
                    entry._texture = AssetDatabase.LoadAssetAtPath<Texture2D>(entry._assetPath);
                }
                if (entry._importer == null || !entry._importer)
                {
                    entry._importer = AssetImporter.GetAtPath(entry._assetPath) as TextureImporter;
                }

                // Create a new list for the new sprites - don't modify entry._sprites yet
                // This keeps old sprites visible if Unity repaints during AssetDatabase operations
                List<SpriteEntryData> newSprites = new List<SpriteEntryData>();

                // Repopulate sprites into the new list
                RepopulateSpritesForEntryIntoList(entry, newSprites);

                // Transfer existing preview textures to new sprites with matching rects
                // This prevents grey question marks during the transition
                for (int i = 0; i < newSprites.Count; ++i)
                {
                    SpriteEntryData sprite = newSprites[i];
                    if (
                        sprite != null
                        && rectToPreview.TryGetValue(sprite._rect, out Texture2D existingPreview)
                    )
                    {
                        sprite._previewTexture = existingPreview;
                        // Remove from dictionary to mark as transferred (not orphaned)
                        rectToPreview.Remove(sprite._rect);
                    }
                }

                // Swap the sprites list FIRST - this is the key fix
                // The new sprites have transferred previews, so they're ready to display
                // Old sprites still have their preview references (not yet cleared)
                entry._sprites = newSprites;

                // NOW clear preview references from old sprites AFTER the swap
                // This prevents double-free issues since the textures are now owned by new sprites or rectToPreview
                // It's safe to clear now because entry._sprites points to newSprites
                if (oldSprites != null)
                {
                    for (int i = 0; i < oldSprites.Count; ++i)
                    {
                        SpriteEntryData sprite = oldSprites[i];
                        if (sprite != null)
                        {
                            sprite._previewTexture = null;
                        }
                    }
                }

                // Reload texture again if it became null during repopulation
                if (entry._texture == null || !entry._texture)
                {
                    entry._texture = AssetDatabase.LoadAssetAtPath<Texture2D>(entry._assetPath);
                }

                // Generate new previews if we have a valid texture
                if (entry._texture != null && entry._sprites != null && entry._sprites.Count > 0)
                {
                    using PooledResource<List<SpriteSheetEntry>> singleEntryLease =
                        Buffers<SpriteSheetEntry>.List.Get(
                            out List<SpriteSheetEntry> singleEntryList
                        );
                    singleEntryList.Add(entry);
                    GenerateAllPreviewTexturesInBatch(singleEntryList);
                }

                // Update cache key to mark entry as fresh (not stale)
                entry._needsRegeneration = false;
                entry._lastCacheKey = entry.GetBoundsCacheKey(this);
                entry._lastAccessTime = DateTime.UtcNow.Ticks;
                if (DiagnosticsEnabled)
                {
                    this.Log(
                        $"SchedulePreviewRegenerationForEntry: updated cache for '{entry._assetPath}', newCacheKey={entry._lastCacheKey}"
                    );
                }

                Repaint();

                // Schedule an additional delayed repaint to ensure the UI updates after any
                // async operations (like texture reimport) complete
                SpriteSheetExtractor windowRef = this;
                EditorApplication.delayCall += () =>
                {
                    if (windowRef)
                    {
                        windowRef.Repaint();
                    }
                };
            }
            finally
            {
                // Destroy orphaned textures (those not transferred to new sprites) even if an exception occurred
                // Use struct enumerator with using statement to properly dispose and avoid allocation
                using PooledResource<List<Texture2D>> orphanedTexturesLease =
                    Buffers<Texture2D>.List.Get(out List<Texture2D> orphanedTextures);
                using (
                    Dictionary<Rect, Texture2D>.Enumerator enumerator =
                        rectToPreview.GetEnumerator()
                )
                {
                    while (enumerator.MoveNext())
                    {
                        KeyValuePair<Rect, Texture2D> kvp = enumerator.Current;
                        if (kvp.Value != null)
                        {
                            orphanedTextures.Add(kvp.Value);
                        }
                    }
                }
                for (int i = 0; i < orphanedTextures.Count; ++i)
                {
                    DestroyImmediate(orphanedTextures[i]);
                }
                if (DiagnosticsEnabled)
                {
                    this.Log(
                        $"SchedulePreviewRegenerationForEntry: END, setting _regenerationInProgress=false"
                    );
                }
                _regenerationInProgress = false;
            }
        }

        /// <summary>
        /// Clears and repopulates the sprite list for a sprite sheet entry based on its effective extraction mode.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method is called by <see cref="RegenerateSpritesForEntry"/> to perform the actual sprite
        /// repopulation. It first clears the existing sprite list (or creates a new one if null), then
        /// populates it based on the entry's effective extraction mode (GridBased, PaddedGrid,
        /// AlphaDetection, or FromMetadata).
        /// </para>
        /// <para>
        /// Unlike <see cref="SchedulePreviewRegenerationForEntry"/>, this method does not preserve
        /// preview textures during repopulation. Use this method for immediate sprite list updates
        /// where preview continuity is not required.
        /// </para>
        /// </remarks>
        /// <param name="entry">The sprite sheet entry to repopulate. If null or has no texture, the method returns immediately.</param>
        internal void RepopulateSpritesForEntry(SpriteSheetEntry entry)
        {
            if (entry == null || entry._texture == null)
            {
                return;
            }

            if (entry._sprites != null)
            {
                entry._sprites.Clear();
            }
            else
            {
                entry._sprites = new List<SpriteEntryData>();
            }

            ExtractionMode effectiveMode = GetEffectiveExtractionMode(entry);

            switch (effectiveMode)
            {
                case ExtractionMode.GridBased:
                    PopulateSpritesFromGrid(entry, entry._texture);
                    break;
                case ExtractionMode.PaddedGrid:
                    PopulateSpritesFromPaddedGrid(entry, entry._texture);
                    break;
                case ExtractionMode.AlphaDetection:
                    PopulateSpritesFromAlphaDetection(entry, entry._texture);
                    break;
                case ExtractionMode.FromMetadata:
                default:
                    PopulateSpritesFromMetadata(entry, entry._assetPath, entry._importer);
                    break;
            }
        }

        /// <summary>
        /// Regenerates sprites for a sprite sheet entry based on its effective extraction mode.
        /// Clears the existing sprite list, repopulates it, and triggers a repaint.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Use this method when the extraction mode changes (e.g., switching from global to per-sheet
        /// settings or changing the extraction mode override) to ensure the sprite list reflects
        /// the new mode immediately.
        /// </para>
        /// <para>
        /// This method provides immediate visual feedback by clearing stale sprite data before
        /// repopulating, preventing outdated rectangles from being drawn in the overlay.
        /// </para>
        /// </remarks>
        /// <param name="entry">The sprite sheet entry to regenerate. If null, the method returns immediately.</param>
        private void RegenerateSpritesForEntry(SpriteSheetEntry entry)
        {
            if (entry == null)
            {
                if (DiagnosticsEnabled)
                {
                    this.Log($"RegenerateSpritesForEntry: entry is null, returning early");
                }
                return;
            }

            if (DiagnosticsEnabled)
            {
                this.Log($"RegenerateSpritesForEntry: START for '{entry._assetPath}'");
            }
            RepopulateSpritesForEntry(entry);
            entry._needsRegeneration = false;
            entry._lastCacheKey = entry.GetBoundsCacheKey(this);
            entry._lastAccessTime = DateTime.UtcNow.Ticks;
            if (DiagnosticsEnabled)
            {
                this.Log(
                    $"RegenerateSpritesForEntry: END for '{entry._assetPath}', spriteCount={entry._sprites?.Count ?? 0}, newCacheKey={entry._lastCacheKey}"
                );
            }
            Repaint();
        }

        /// <summary>
        /// Marks a sprite sheet entry as needing regeneration.
        /// The actual regeneration is deferred until the entry is next accessed.
        /// </summary>
        /// <param name="entry">The sprite sheet entry to invalidate. If null, the method returns immediately.</param>
        internal void InvalidateEntry(SpriteSheetEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            if (DiagnosticsEnabled)
            {
                this.Log($"InvalidateEntry: marking '{entry._assetPath}' for regeneration");
            }

            entry._needsRegeneration = true;
            entry._cachedAlgorithmResult = null;
            entry._lastAlgorithmDisplayText = null;
        }

        /// <summary>
        /// Checks if an entry's cached sprite data is stale and needs regeneration.
        /// </summary>
        /// <param name="entry">The sprite sheet entry to check.</param>
        /// <returns>True if the entry's cache is stale, false otherwise.</returns>
        internal bool IsEntryStale(SpriteSheetEntry entry)
        {
            if (entry == null)
            {
                return false;
            }

            if (entry._needsRegeneration)
            {
                if (DiagnosticsEnabled)
                {
                    this.Log(
                        $"IsEntryStale: '{entry._assetPath}' is stale because _needsRegeneration=true"
                    );
                }
                return true;
            }

            int currentCacheKey = entry.GetBoundsCacheKey(this);
            bool isStale = entry._lastCacheKey != currentCacheKey;
            if (isStale && DiagnosticsEnabled)
            {
                this.Log(
                    $"IsEntryStale: '{entry._assetPath}' is stale because cacheKey mismatch (stored={entry._lastCacheKey}, current={currentCacheKey})"
                );
            }
            return isStale;
        }

        /// <summary>
        /// Checks if an entry's cache is stale and regenerates if needed.
        /// Call this before accessing sprite data to ensure freshness.
        /// </summary>
        /// <param name="entry">The sprite sheet entry to check.</param>
        private void CheckAndRegenerateIfNeeded(SpriteSheetEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            int currentCacheKey = entry.GetBoundsCacheKey(this);
            if (entry._needsRegeneration || entry._lastCacheKey != currentCacheKey)
            {
                if (DiagnosticsEnabled)
                {
                    this.Log(
                        $"CheckAndRegenerateIfNeeded: regenerating '{entry._assetPath}' (needsRegeneration={entry._needsRegeneration}, cacheKeyMismatch={entry._lastCacheKey != currentCacheKey})"
                    );
                }
                RegenerateSpritesForEntry(entry);
                CheckAndEvictLRUCache();
            }
            else
            {
                entry._lastAccessTime = DateTime.UtcNow.Ticks;
            }
        }

        /// <summary>
        /// Invalidates all entries that are using global settings.
        /// Call this when global extraction settings change.
        /// </summary>
        internal void InvalidateEntriesUsingGlobalSettings()
        {
            if (_discoveredSheets == null)
            {
                return;
            }

            for (int i = 0; i < _discoveredSheets.Count; ++i)
            {
                SpriteSheetEntry entry = _discoveredSheets[i];
                if (entry != null && entry._useGlobalSettings)
                {
                    InvalidateEntry(entry);
                }
            }

            Repaint();
        }

        /// <summary>
        /// Regenerates previews for all entries that are using global settings.
        /// Call this when global extraction settings change to immediately update previews.
        /// </summary>
        private void RegenerateEntriesUsingGlobalSettings()
        {
            if (DiagnosticsEnabled)
            {
                this.Log($"RegenerateEntriesUsingGlobalSettings: START");
            }
            if (_discoveredSheets == null)
            {
                if (DiagnosticsEnabled)
                {
                    this.Log(
                        $"RegenerateEntriesUsingGlobalSettings: _discoveredSheets is null, returning early"
                    );
                }
                return;
            }

            int regeneratedCount = 0;
            for (int i = 0; i < _discoveredSheets.Count; ++i)
            {
                SpriteSheetEntry entry = _discoveredSheets[i];
                if (entry != null && entry._useGlobalSettings)
                {
                    if (DiagnosticsEnabled)
                    {
                        this.Log(
                            $"RegenerateEntriesUsingGlobalSettings: regenerating entry '{entry._assetPath}'"
                        );
                    }
                    regeneratedCount++;
                    entry._cachedAlgorithmResult = null;
                    // Use SchedulePreviewRegenerationForEntry instead of RegenerateSpritesForEntry
                    // to preserve and regenerate preview textures when algorithm changes
                    SchedulePreviewRegenerationForEntry(entry);
                }
            }
            if (DiagnosticsEnabled)
            {
                this.Log(
                    $"RegenerateEntriesUsingGlobalSettings: END, regenerated {regeneratedCount} entries"
                );
            }
        }

        /// <summary>
        /// Invalidates all discovered entries regardless of settings mode.
        /// Call this when texture-affecting settings change.
        /// </summary>
        internal void InvalidateAllEntries()
        {
            if (_discoveredSheets == null)
            {
                return;
            }

            for (int i = 0; i < _discoveredSheets.Count; ++i)
            {
                SpriteSheetEntry entry = _discoveredSheets[i];
                if (entry != null)
                {
                    InvalidateEntry(entry);
                }
            }

            Repaint();
        }

        /// <summary>
        /// Enables pivot overrides for all sprites in the given entry.
        /// </summary>
        /// <param name="entry">The sprite sheet entry to modify.</param>
        internal void EnableAllPivotOverrides(SpriteSheetEntry entry)
        {
            if (entry == null || entry._sprites == null)
            {
                return;
            }

            for (int i = 0; i < entry._sprites.Count; ++i)
            {
                SpriteEntryData sprite = entry._sprites[i];
                if (sprite != null)
                {
                    sprite._usePivotOverride = true;
                }
            }
        }

        /// <summary>
        /// Disables pivot overrides for all sprites in the given entry.
        /// </summary>
        /// <param name="entry">The sprite sheet entry to modify.</param>
        internal void DisableAllPivotOverrides(SpriteSheetEntry entry)
        {
            if (entry == null || entry._sprites == null)
            {
                return;
            }

            for (int i = 0; i < entry._sprites.Count; ++i)
            {
                SpriteEntryData sprite = entry._sprites[i];
                if (sprite != null)
                {
                    sprite._usePivotOverride = false;
                }
            }
        }

        /// <summary>
        /// Schedules preview regeneration for a sprite sheet entry.
        /// This is a public wrapper around the internal regeneration method.
        /// </summary>
        /// <param name="entry">The sprite sheet entry to regenerate previews for.</param>
        internal void SchedulePreviewRegeneration(SpriteSheetEntry entry)
        {
            SchedulePreviewRegenerationForEntry(entry);
        }

        /// <summary>
        /// Computes a bounds cache key for an entry.
        /// This is an instance method wrapper that delegates to the entry's cache key computation.
        /// </summary>
        /// <param name="entry">The sprite sheet entry to compute a cache key for. Can be null.</param>
        /// <returns>A hash code representing the current configuration state.</returns>
        internal int GetBoundsCacheKey(SpriteSheetEntry entry)
        {
            if (entry == null)
            {
                return GetBoundsCacheKeyStatic(this, null);
            }

            return entry.GetBoundsCacheKey(this);
        }

        /// <summary>
        /// Computes a bounds cache key using static parameters.
        /// Used for null-safe cache key computation when either extractor or entry may be null.
        /// </summary>
        /// <param name="extractor">The extractor to read global settings from. Can be null.</param>
        /// <param name="entry">The sprite sheet entry to compute a cache key for. Can be null.</param>
        /// <returns>A hash code representing the current configuration state, or 0 if extractor is null.</returns>
        internal static int GetBoundsCacheKeyStatic(
            SpriteSheetExtractor extractor,
            SpriteSheetEntry entry
        )
        {
            if (extractor == null)
            {
                return 0;
            }

            if (entry == null)
            {
                return Objects.HashCode(
                    extractor._extractionMode,
                    extractor._gridSizeMode,
                    extractor._gridColumns,
                    extractor._gridRows,
                    extractor._cellWidth,
                    extractor._cellHeight,
                    extractor._paddingLeft,
                    extractor._paddingRight,
                    extractor._paddingTop,
                    extractor._paddingBottom,
                    extractor._alphaThreshold,
                    extractor._autoDetectionAlgorithm,
                    extractor._expectedSpriteCountHint,
                    extractor._snapToTextureDivisor
                );
            }

            return entry.GetBoundsCacheKey(extractor);
        }

        /// <summary>
        /// Checks cache size and evicts least recently used entries if limit is exceeded.
        /// Entries are evicted by clearing their sprite lists and preview textures.
        /// </summary>
        internal void CheckAndEvictLRUCache()
        {
            if (_discoveredSheets == null || _discoveredSheets.Count <= MaxCachedEntries)
            {
                return;
            }

            int cachedCount = 0;
            for (int i = 0; i < _discoveredSheets.Count; ++i)
            {
                SpriteSheetEntry entry = _discoveredSheets[i];
                if (entry != null && entry._sprites != null && entry._sprites.Count > 0)
                {
                    ++cachedCount;
                }
            }

            if (cachedCount <= MaxCachedEntries)
            {
                return;
            }

            int entriesToEvict = cachedCount - MaxCachedEntries;

            using PooledResource<List<SpriteSheetEntry>> sortedEntriesLease =
                Buffers<SpriteSheetEntry>.List.Get(out List<SpriteSheetEntry> sortedEntries);

            for (int i = 0; i < _discoveredSheets.Count; ++i)
            {
                SpriteSheetEntry entry = _discoveredSheets[i];
                if (entry != null && entry._sprites != null && entry._sprites.Count > 0)
                {
                    sortedEntries.Add(entry);
                }
            }

            sortedEntries.Sort((a, b) => a._lastAccessTime.CompareTo(b._lastAccessTime));

            for (int i = 0; i < entriesToEvict && i < sortedEntries.Count; ++i)
            {
                SpriteSheetEntry entry = sortedEntries[i];
                EvictEntry(entry);
            }
        }

        /// <summary>
        /// Evicts a single entry by clearing its sprite list and preview textures.
        /// The entry remains in the discovered sheets list but its cache is cleared.
        /// </summary>
        /// <param name="entry">The entry to evict.</param>
        private void EvictEntry(SpriteSheetEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            if (entry._sprites != null)
            {
                for (int i = 0; i < entry._sprites.Count; ++i)
                {
                    SpriteEntryData sprite = entry._sprites[i];
                    if (sprite != null && sprite._previewTexture != null)
                    {
                        DestroyImmediate(sprite._previewTexture);
                        sprite._previewTexture = null;
                    }
                }
                entry._sprites.Clear();
            }

            entry._needsRegeneration = true;
            entry._cachedAlgorithmResult = null;
            entry._lastAlgorithmDisplayText = null;
        }

        /// <summary>
        /// Repopulates sprites for a sprite sheet entry into a provided target list.
        /// IMPORTANT: This method does NOT modify entry._sprites at all, ensuring that old sprites
        /// remain visible if Unity repaints during AssetDatabase operations.
        /// </summary>
        /// <param name="entry">The sprite sheet entry to get configuration from.</param>
        /// <param name="targetList">The list to populate sprites into.</param>
        internal void RepopulateSpritesForEntryIntoList(
            SpriteSheetEntry entry,
            List<SpriteEntryData> targetList
        )
        {
            if (entry == null || entry._texture == null || targetList == null)
            {
                return;
            }

            ExtractionMode effectiveMode = GetEffectiveExtractionMode(entry);

            switch (effectiveMode)
            {
                case ExtractionMode.GridBased:
                    PopulateSpritesFromGridIntoList(entry, entry._texture, targetList);
                    break;
                case ExtractionMode.PaddedGrid:
                    PopulateSpritesFromPaddedGridIntoList(entry, entry._texture, targetList);
                    break;
                case ExtractionMode.AlphaDetection:
                    PopulateSpritesFromAlphaDetectionIntoList(entry, entry._texture, targetList);
                    break;
                case ExtractionMode.FromMetadata:
                default:
                    PopulateSpritesFromMetadataIntoList(
                        entry,
                        entry._assetPath,
                        entry._importer,
                        targetList
                    );
                    break;
            }
        }

        private void DrawSourceTexturePreview(SpriteSheetEntry entry)
        {
            entry._sourcePreviewExpanded = EditorGUILayout.Foldout(
                entry._sourcePreviewExpanded,
                "Source Texture Preview",
                true
            );

            if (!entry._sourcePreviewExpanded)
            {
                return;
            }

            if (entry._texture == null)
            {
                EditorGUILayout.LabelField("Texture not available.", EditorStyles.miniLabel);
                return;
            }

            entry._editPivotsMode = EditorGUILayout.Toggle(
                new GUIContent(
                    "Edit Pivots",
                    "Click empty area: Set sheet pivot | Click sprite: Set sprite pivot | Drag marker: Fine-tune"
                ),
                entry._editPivotsMode
            );

            if (entry._editPivotsMode)
            {
                EditorGUILayout.HelpBox(
                    "Click empty area: Set sheet pivot | Click sprite: Set sprite pivot | Drag marker: Fine-tune",
                    MessageType.Info
                );
            }

            int textureWidth = entry._texture.width;
            int textureHeight = entry._texture.height;

            float maxPreviewWidth = EditorGUIUtility.currentViewWidth - 50;
            float maxPreviewHeight = 200;
            float scale = Mathf.Min(
                maxPreviewWidth / textureWidth,
                maxPreviewHeight / textureHeight
            );
            scale = Mathf.Min(scale, 1f);

            float displayWidth = textureWidth * scale;
            float displayHeight = textureHeight * scale;

            Rect previewRect = GUILayoutUtility.GetRect(displayWidth, displayHeight);

            bool isStale = IsEntryStale(entry);

            Color previousColor = GUI.color;
            if (isStale)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.5f);
            }

            GUI.DrawTexture(previewRect, entry._texture, ScaleMode.ScaleToFit);

            if (isStale)
            {
                GUI.color = previousColor;
                GUIStyle centerStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(1f, 0.8f, 0f, 0.9f) },
                };
                GUI.Label(previewRect, "Regenerating...", centerStyle);
            }

            bool effectiveShowOverlay = GetEffectiveShowOverlay(entry);
            if (effectiveShowOverlay)
            {
                if (entry._sprites != null && entry._sprites.Count > 0)
                {
                    DrawSpriteBoundsOverlay(
                        previewRect,
                        entry._texture.width,
                        entry._texture.height,
                        scale,
                        entry
                    );
                }
                else
                {
                    // Draw grid overlay based on current grid settings when sprites aren't available
                    DrawGridOverlayFromSettings(
                        previewRect,
                        textureWidth,
                        textureHeight,
                        scale,
                        entry
                    );
                }
            }

            if (entry._editPivotsMode)
            {
                Rect textureRect = CalculateTextureRectWithinPreview(
                    previewRect,
                    textureWidth,
                    textureHeight,
                    scale
                );

                HandlePivotEditingEvents(entry, textureRect, textureHeight, scale);
                DrawSheetLevelPivotMarker(entry, textureRect);
            }
        }

        /// <summary>
        /// Handles mouse events for pivot editing when edit mode is active.
        /// </summary>
        private void HandlePivotEditingEvents(
            SpriteSheetEntry entry,
            Rect textureRect,
            int textureHeight,
            float scale
        )
        {
            Event current = Event.current;
            Vector2 mousePosition = current.mousePosition;

            if (!textureRect.Contains(mousePosition) && !_isDraggingPivot)
            {
                ClearPivotHoverState();
                return;
            }

            int controlId = GUIUtility.GetControlID(FocusType.Passive);

            switch (current.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (current.button == 0 && textureRect.Contains(mousePosition))
                    {
                        HandlePivotMouseDown(
                            entry,
                            textureRect,
                            textureHeight,
                            scale,
                            mousePosition,
                            controlId
                        );
                        current.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (_isDraggingPivot && GUIUtility.hotControl == controlId)
                    {
                        HandlePivotMouseDrag(
                            entry,
                            textureRect,
                            textureHeight,
                            scale,
                            mousePosition
                        );
                        current.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (_isDraggingPivot && GUIUtility.hotControl == controlId)
                    {
                        HandlePivotMouseUp();
                        current.Use();
                    }
                    break;

                case EventType.MouseMove:
                    UpdatePivotHoverState(entry, textureRect, textureHeight, scale, mousePosition);
                    break;

                case EventType.Repaint:
                    if (_isHoveringPivot || _isDraggingPivot)
                    {
                        if (!EditorUi.Suppress)
                        {
                            EditorGUIUtility.AddCursorRect(textureRect, MouseCursor.Pan);
                        }
                    }
                    break;
            }
        }

        private void HandlePivotMouseDown(
            SpriteSheetEntry entry,
            Rect textureRect,
            int textureHeight,
            float scale,
            Vector2 mousePosition,
            int controlId
        )
        {
            GUIUtility.hotControl = controlId;
            _isDraggingPivot = true;
            _draggedPivotTarget = entry;

            int spriteIndex = FindSpriteAtScreenPosition(
                mousePosition,
                textureRect,
                entry,
                textureHeight,
                scale
            );

            if (spriteIndex >= 0)
            {
                _draggedPivotType = PivotDragType.Sprite;
                _draggedSpriteIndex = spriteIndex;

                SpriteEntryData sprite = entry._sprites[spriteIndex];
                Rect spriteScreenRect = ConvertTextureRectToScreenRect(
                    textureRect,
                    sprite._rect,
                    textureHeight,
                    scale
                );
                Vector2 normalizedPivot = CalculateNormalizedPositionInSprite(
                    mousePosition,
                    spriteScreenRect
                );

                sprite._usePivotOverride = true;
                sprite._pivotModeOverride = PivotMode.Custom;
                sprite._customPivotOverride = normalizedPivot;
            }
            else
            {
                _draggedPivotType = PivotDragType.Sheet;
                _draggedSpriteIndex = -1;

                Vector2 normalizedPivot = CalculateNormalizedPositionInSprite(
                    mousePosition,
                    textureRect
                );

                entry._useGlobalSettings = false;
                entry._pivotModeOverride = PivotMode.Custom;
                entry._customPivotOverride = normalizedPivot;
            }

            Repaint();
        }

        private void HandlePivotMouseDrag(
            SpriteSheetEntry entry,
            Rect textureRect,
            int textureHeight,
            float scale,
            Vector2 mousePosition
        )
        {
            if (_draggedPivotTarget != entry)
            {
                return;
            }

            if (_draggedPivotType == PivotDragType.Sprite)
            {
                if (_draggedSpriteIndex >= 0 && _draggedSpriteIndex < entry._sprites.Count)
                {
                    SpriteEntryData sprite = entry._sprites[_draggedSpriteIndex];
                    Rect spriteScreenRect = ConvertTextureRectToScreenRect(
                        textureRect,
                        sprite._rect,
                        textureHeight,
                        scale
                    );
                    Vector2 normalizedPivot = CalculateNormalizedPositionInSprite(
                        mousePosition,
                        spriteScreenRect
                    );

                    sprite._customPivotOverride = normalizedPivot;
                }
            }
            else if (_draggedPivotType == PivotDragType.Sheet)
            {
                Vector2 normalizedPivot = CalculateNormalizedPositionInSprite(
                    mousePosition,
                    textureRect
                );

                entry._customPivotOverride = normalizedPivot;
            }

            Repaint();
        }

        private void HandlePivotMouseUp()
        {
            GUIUtility.hotControl = 0;
            _isDraggingPivot = false;
            _draggedPivotTarget = null;
#pragma warning disable CS0618 // PivotDragType.None is Obsolete
            _draggedPivotType = PivotDragType.None;
#pragma warning restore CS0618
            _draggedSpriteIndex = -1;
            Repaint();
        }

        private void UpdatePivotHoverState(
            SpriteSheetEntry entry,
            Rect textureRect,
            int textureHeight,
            float scale,
            Vector2 mousePosition
        )
        {
            int spriteIndex = FindSpriteAtScreenPosition(
                mousePosition,
                textureRect,
                entry,
                textureHeight,
                scale
            );

            bool wasHovering = _isHoveringPivot;
            int previousSpriteIndex = _hoveredSpriteIndex;
            _isHoveringPivot = true;
            _hoveredPivotTarget = entry;
            _hoveredSpriteIndex = spriteIndex;

            if (wasHovering != _isHoveringPivot || previousSpriteIndex != _hoveredSpriteIndex)
            {
                Repaint();
            }
        }

        private void ClearPivotHoverState()
        {
            if (_isHoveringPivot)
            {
                _isHoveringPivot = false;
                _hoveredPivotTarget = null;
                _hoveredSpriteIndex = -1;
                Repaint();
            }
        }

        internal void CalculateGridDimensions(
            int textureWidth,
            int textureHeight,
            out int columns,
            out int rows,
            out int cellWidth,
            out int cellHeight
        )
        {
            CalculateGridDimensions(
                textureWidth,
                textureHeight,
                null,
                out columns,
                out rows,
                out cellWidth,
                out cellHeight
            );
        }

        internal void CalculateGridDimensions(
            int textureWidth,
            int textureHeight,
            SpriteSheetEntry entry,
            out int columns,
            out int rows,
            out int cellWidth,
            out int cellHeight
        )
        {
            CalculateGridDimensions(
                textureWidth,
                textureHeight,
                entry,
                null,
                out columns,
                out rows,
                out cellWidth,
                out cellHeight
            );
        }

        /// <summary>
        /// Calculates grid dimensions for sprite extraction.
        /// In Manual mode, derives cell size from columns/rows.
        /// In Auto mode, detects grid from transparency or uses fallback heuristics.
        /// </summary>
        internal void CalculateGridDimensions(
            int textureWidth,
            int textureHeight,
            SpriteSheetEntry entry,
            Color32[] pixels,
            out int columns,
            out int rows,
            out int cellWidth,
            out int cellHeight
        )
        {
            GridSizeMode effectiveGridSizeMode = GetEffectiveGridSizeMode(entry);
            int effectiveGridColumns = GetEffectiveGridColumns(entry);
            int effectiveGridRows = GetEffectiveGridRows(entry);
            int effectiveCellWidth = GetEffectiveCellWidth(entry);
            int effectiveCellHeight = GetEffectiveCellHeight(entry);

            if (effectiveGridSizeMode == GridSizeMode.Manual)
            {
                columns = Mathf.Max(1, effectiveGridColumns);
                rows = Mathf.Max(1, effectiveGridRows);

                // In Manual mode, always derive cell size from columns/rows
                cellWidth = textureWidth / columns;
                cellHeight = textureHeight / rows;
            }
            else
            {
                float effectiveAlphaThreshold = GetEffectiveAlphaThreshold(entry);
                AutoDetectionAlgorithm algorithm = GetEffectiveAutoDetectionAlgorithm(entry);
                int expectedSpriteCount = GetEffectiveExpectedSpriteCount(entry);
                bool detectedFromAlgorithm = false;
                cellWidth = 0;
                cellHeight = 0;

                // Try to use cached result if available and valid (check BEFORE pixels check
                // so cached results work even when called without pixel data)
                if (entry != null && entry._cachedAlgorithmResult.HasValue)
                {
                    SpriteSheetAlgorithms.AlgorithmResult cached = entry
                        ._cachedAlgorithmResult
                        .Value;
                    if (cached.IsValid)
                    {
                        cellWidth = cached.CellWidth;
                        cellHeight = cached.CellHeight;
                        detectedFromAlgorithm = true;
                        entry._lastAlgorithmDisplayText =
                            $"{cached.Algorithm}: {cached.Confidence:P0}";
                    }
                }

                // If no cached result and pixels available, run algorithm detection
                if (
                    !detectedFromAlgorithm
                    && pixels != null
                    && pixels.Length == textureWidth * textureHeight
                )
                {
                    bool snapToTextureDivisor = GetEffectiveSnapToTextureDivisor(entry);
                    SpriteSheetAlgorithms.AlgorithmResult result = SpriteSheetAlgorithms.DetectGrid(
                        pixels,
                        textureWidth,
                        textureHeight,
                        effectiveAlphaThreshold,
                        algorithm,
                        expectedSpriteCount,
                        snapToTextureDivisor
                    );

                    if (DiagnosticsEnabled && entry != null)
                    {
                        this.Log(
                            $"Algorithm detection for '{Path.GetFileName(entry._assetPath)}': algorithm={algorithm}, expectedSpriteCount={expectedSpriteCount}, textureSize={textureWidth}x{textureHeight}, isValid={result.IsValid}, cellSize={result.CellWidth}x{result.CellHeight}, confidence={result.Confidence:P0}"
                        );
                    }

                    if (result.IsValid)
                    {
                        cellWidth = result.CellWidth;
                        cellHeight = result.CellHeight;
                        detectedFromAlgorithm = true;
                        if (entry != null)
                        {
                            entry._cachedAlgorithmResult = result;
                            entry._lastAlgorithmDisplayText =
                                $"{result.Algorithm}: {result.Confidence:P0}";
                        }

                        // Task 5: Verify grid does not cut through sprites after successful detection
                        // IMPORTANT: Skip this verification when user has specified expectedSpriteCount,
                        // because the user explicitly told us how many sprites they want and we should trust that.
                        // The verification can incorrectly fail for sprites with anti-aliasing or shadows.
                        bool skipVerification = expectedSpriteCount > 0;
                        if (
                            !skipVerification
                            && !VerifyGridDoesNotCutSprites(
                                pixels,
                                textureWidth,
                                textureHeight,
                                cellWidth,
                                cellHeight,
                                effectiveAlphaThreshold
                            )
                        )
                        {
                            // Grid cuts sprites - try region-based detection as alternative
                            (int regionCellWidth, int regionCellHeight) =
                                DetectCellSizeFromOpaqueRegions(
                                    pixels,
                                    textureWidth,
                                    textureHeight,
                                    effectiveAlphaThreshold
                                );

                            if (DiagnosticsEnabled && entry != null)
                            {
                                this.Log(
                                    $"VerifyGridDoesNotCutSprites FAILED for '{Path.GetFileName(entry._assetPath)}': original cellSize={cellWidth}x{cellHeight}, region detection returned {regionCellWidth}x{regionCellHeight}"
                                );
                            }

                            if (regionCellWidth > 0 && regionCellHeight > 0)
                            {
                                cellWidth = regionCellWidth;
                                cellHeight = regionCellHeight;
                                if (entry != null)
                                {
                                    entry._lastAlgorithmDisplayText =
                                        $"{result.Algorithm}: {result.Confidence:P0} (adjusted)";
                                }
                            }
                        }
                    }
                }

                if (!detectedFromAlgorithm)
                {
                    // Task 4: First try region-based detection for accurate cell size detection
                    (int regionWidth, int regionHeight) = DetectCellSizeFromOpaqueRegions(
                        pixels,
                        textureWidth,
                        textureHeight,
                        effectiveAlphaThreshold
                    );

                    if (regionWidth > 0 && regionHeight > 0)
                    {
                        cellWidth = regionWidth;
                        cellHeight = regionHeight;
                        if (entry != null)
                        {
                            entry._lastAlgorithmDisplayText = "Fallback (region analysis)";
                        }
                    }
                    else
                    {
                        // Use smarter fallback that prefers common sprite sizes over GCD
                        cellWidth = FindSmallestReasonableDivisor(textureWidth);
                        cellHeight = FindSmallestReasonableDivisor(textureHeight);

                        // If both return the full dimension, try using GCD as a fallback
                        if (cellWidth == textureWidth && cellHeight == textureHeight)
                        {
                            int gcd = CalculateGCD(textureWidth, textureHeight);
                            if (gcd >= 8 && gcd < textureWidth && gcd < textureHeight)
                            {
                                cellWidth = gcd;
                                cellHeight = gcd;
                            }
                        }

                        if (entry != null)
                        {
                            entry._lastAlgorithmDisplayText = "Fallback (divisor heuristic)";
                        }
                    }
                }

                // Only recalculate cell dimensions if they don't evenly divide the texture
                // This preserves algorithm-detected values when they're already valid
                if (textureWidth % cellWidth == 0)
                {
                    columns = textureWidth / cellWidth;
                }
                else
                {
                    columns = Mathf.Max(1, textureWidth / cellWidth);
                    cellWidth = textureWidth / columns;
                }

                if (textureHeight % cellHeight == 0)
                {
                    rows = textureHeight / cellHeight;
                }
                else
                {
                    rows = Mathf.Max(1, textureHeight / cellHeight);
                    cellHeight = textureHeight / rows;
                }

                if (DiagnosticsEnabled && entry != null)
                {
                    this.Log(
                        $"CalculateGridDimensions FINAL for '{Path.GetFileName(entry._assetPath)}': columns={columns}, rows={rows}, cellWidth={cellWidth}, cellHeight={cellHeight}, detectedFromAlgorithm={detectedFromAlgorithm}"
                    );
                }
            }
        }

        private static int CalculateGCD(int a, int b)
        {
            while (b != 0)
            {
                int temp = b;
                b = a % b;
                a = temp;
            }
            return a;
        }

        /// <summary>
        /// Finds the smallest reasonable divisor for a dimension, preferring common sprite sizes.
        /// This produces better results than GCD for typical sprite sheets.
        /// </summary>
        /// <param name="dimension">The texture dimension to find a divisor for.</param>
        /// <returns>A divisor that produces at least 2 cells if possible, or the dimension itself.</returns>
        internal static int FindSmallestReasonableDivisor(int dimension)
        {
            // First try common sprite sizes that produce at least 2 cells
            for (int i = 0; i < CommonCellSizes.Length; ++i)
            {
                int size = CommonCellSizes[i];
                if (size >= 8 && size <= dimension && dimension % size == 0)
                {
                    int cellCount = dimension / size;
                    if (cellCount >= 2)
                    {
                        return size;
                    }
                }
            }

            // If no common size works, try all divisors starting from 8
            for (int divisor = 8; divisor <= dimension / 2; ++divisor)
            {
                if (dimension % divisor == 0)
                {
                    return divisor;
                }
            }

            // Last resort: return the full dimension (1 cell)
            return dimension;
        }

        /// <summary>
        /// Detects optimal grid dimensions by analyzing transparent boundaries in the texture.
        /// Uses a multi-candidate scoring approach that evaluates various cell sizes and picks
        /// the one with the highest transparency score along its grid boundaries.
        /// </summary>
        /// <param name="pixels">The texture pixel data in Color32 format.</param>
        /// <param name="textureWidth">Width of the texture in pixels.</param>
        /// <param name="textureHeight">Height of the texture in pixels.</param>
        /// <param name="alphaThreshold">Alpha value (0-1) below which a pixel is considered transparent.
        /// Must be in range [0.0, 1.0); values outside this range return false immediately.</param>
        /// <param name="cellWidth">Output: Detected cell width, or 0 if no clear grid was detected.</param>
        /// <param name="cellHeight">Output: Detected cell height, or 0 if no clear grid was detected.</param>
        /// <returns>True if a valid grid was detected, false otherwise.</returns>
        internal static bool DetectOptimalGridFromTransparency(
            Color32[] pixels,
            int textureWidth,
            int textureHeight,
            float alphaThreshold,
            out int cellWidth,
            out int cellHeight
        )
        {
            cellWidth = 0;
            cellHeight = 0;

            if (pixels == null || pixels.Length == 0)
            {
                return false;
            }

            if (
                textureWidth < SpriteSheetAlgorithms.MinimumCellSize
                || textureHeight < SpriteSheetAlgorithms.MinimumCellSize
            )
            {
                return false;
            }

            if (pixels.Length != textureWidth * textureHeight)
            {
                return false;
            }

            if (alphaThreshold < 0f || alphaThreshold >= 1f)
            {
                return false;
            }

            byte alphaThresholdByte = (byte)(alphaThreshold * 255f);

            using PooledArray<int> columnTransparencyLease = SystemArrayPool<int>.Get(
                textureWidth,
                out int[] columnTransparencyCount
            );
            using PooledArray<int> rowTransparencyLease = SystemArrayPool<int>.Get(
                textureHeight,
                out int[] rowTransparencyCount
            );

            Array.Clear(columnTransparencyCount, 0, textureWidth);
            Array.Clear(rowTransparencyCount, 0, textureHeight);

            int totalTransparent = 0;
            for (int y = 0; y < textureHeight; ++y)
            {
                int rowOffset = y * textureWidth;
                for (int x = 0; x < textureWidth; ++x)
                {
                    if (pixels[rowOffset + x].a <= alphaThresholdByte)
                    {
                        ++columnTransparencyCount[x];
                        ++rowTransparencyCount[y];
                        ++totalTransparent;
                    }
                }
            }

            // Fully opaque or fully transparent textures have no meaningful grid
            int totalPixels = textureWidth * textureHeight;
            if (totalTransparent == 0 || totalTransparent == totalPixels)
            {
                return false;
            }

            using PooledResource<List<int>> widthCandidatesLease = Buffers<int>.List.Get(
                out List<int> widthCandidates
            );
            using PooledResource<List<int>> heightCandidatesLease = Buffers<int>.List.Get(
                out List<int> heightCandidates
            );

            GenerateCandidateCellSizes(textureWidth, widthCandidates);
            GenerateCandidateCellSizes(textureHeight, heightCandidates);

            int bestWidth = 0;
            int bestHeight = 0;
            float bestScore = -1f;

            for (int wi = 0; wi < widthCandidates.Count; ++wi)
            {
                int candidateWidth = widthCandidates[wi];
                float widthScore = ScoreCellSizeForDimension(
                    columnTransparencyCount,
                    textureWidth,
                    textureHeight,
                    candidateWidth
                );

                if (widthScore < MinimumBoundaryScore)
                {
                    continue;
                }

                for (int hi = 0; hi < heightCandidates.Count; ++hi)
                {
                    int candidateHeight = heightCandidates[hi];
                    float heightScore = ScoreCellSizeForDimension(
                        rowTransparencyCount,
                        textureHeight,
                        textureWidth,
                        candidateHeight
                    );

                    if (heightScore < MinimumBoundaryScore)
                    {
                        continue;
                    }

                    float combinedScore = (widthScore + heightScore) * 0.5f;

                    int columns = textureWidth / candidateWidth;
                    int rows = textureHeight / candidateHeight;

                    // Stronger bonus for producing multiple cells in both dimensions
                    if (columns >= 2 && rows >= 2)
                    {
                        combinedScore += 0.15f;
                    }
                    else if (columns >= 2 || rows >= 2)
                    {
                        combinedScore += 0.08f;
                    }

                    if (IsPowerOfTwo(candidateWidth) && IsPowerOfTwo(candidateHeight))
                    {
                        combinedScore += 0.05f;
                    }

                    if (candidateWidth == candidateHeight)
                    {
                        combinedScore += 0.02f;
                    }

                    // Bonus for reasonable cell counts (4-64 cells is a sweet spot)
                    int cellCount = columns * rows;
                    if (cellCount >= 4 && cellCount <= 64)
                    {
                        combinedScore += 0.03f;
                    }

                    // Prefer smaller cell sizes when scores are very close (within epsilon)
                    // This ensures more granular sprites when both sizes are equally valid
                    const float scoreEpsilon = 0.01f;
                    bool significantlyBetter = combinedScore > bestScore + scoreEpsilon;
                    bool essentiallyEqual =
                        !significantlyBetter
                        && combinedScore >= bestScore - scoreEpsilon
                        && combinedScore <= bestScore + scoreEpsilon;
                    bool smallerCellSize =
                        candidateWidth < bestWidth || candidateHeight < bestHeight;

                    if (significantlyBetter || (essentiallyEqual && smallerCellSize))
                    {
                        bestScore = combinedScore;
                        bestWidth = candidateWidth;
                        bestHeight = candidateHeight;
                    }
                }
            }

            if (bestScore >= MinimumBoundaryScore && bestWidth > 0 && bestHeight > 0)
            {
                cellWidth = bestWidth;
                cellHeight = bestHeight;
                return true;
            }

            int detectedCellWidth = FindConsistentSpacingFromTransparency(
                columnTransparencyCount,
                textureWidth,
                textureHeight,
                minimumCellSize: 8
            );
            int detectedCellHeight = FindConsistentSpacingFromTransparency(
                rowTransparencyCount,
                textureHeight,
                textureWidth,
                minimumCellSize: 8
            );

            if (detectedCellWidth > 0 && detectedCellHeight > 0)
            {
                cellWidth = detectedCellWidth;
                cellHeight = detectedCellHeight;
                return true;
            }

            if (detectedCellWidth > 0 && detectedCellHeight <= 0)
            {
                cellWidth = detectedCellWidth;
                cellHeight = detectedCellWidth;
                if (textureHeight % cellHeight == 0)
                {
                    return true;
                }
            }

            if (detectedCellHeight > 0 && detectedCellWidth <= 0)
            {
                cellHeight = detectedCellHeight;
                cellWidth = detectedCellHeight;
                if (textureWidth % cellWidth == 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Generates a list of candidate cell sizes for a given dimension.
        /// Includes common sprite sizes, power-of-2 sizes, and all divisors >= 8.
        /// </summary>
        private static void GenerateCandidateCellSizes(int dimension, List<int> candidates)
        {
            candidates.Clear();

            for (int i = 0; i < CommonCellSizes.Length; ++i)
            {
                int size = CommonCellSizes[i];
                if (size <= dimension && dimension % size == 0)
                {
                    AddUniqueSorted(candidates, size);
                }
            }

            for (int power = 3; power <= 10; ++power)
            {
                int size = 1 << power;
                if (size > dimension)
                {
                    break;
                }
                if (dimension % size == 0)
                {
                    AddUniqueSorted(candidates, size);
                }
            }

            for (int divisor = 2; divisor * divisor <= dimension; ++divisor)
            {
                if (dimension % divisor == 0)
                {
                    if (divisor >= 8)
                    {
                        AddUniqueSorted(candidates, divisor);
                    }
                    int complement = dimension / divisor;
                    if (complement >= 8 && complement != divisor)
                    {
                        AddUniqueSorted(candidates, complement);
                    }
                }
            }

            if (dimension >= 8)
            {
                AddUniqueSorted(candidates, dimension);
            }
        }

        /// <summary>
        /// Adds a value to a sorted list if not already present, maintaining sort order.
        /// </summary>
        private static void AddUniqueSorted(List<int> list, int value)
        {
            int insertIndex = 0;
            for (int i = 0; i < list.Count; ++i)
            {
                if (list[i] == value)
                {
                    return;
                }
                if (list[i] < value)
                {
                    insertIndex = i + 1;
                }
            }
            list.Insert(insertIndex, value);
        }

        /// <summary>
        /// Scores a candidate cell size based on how transparent the grid boundaries would be.
        /// Returns a value between 0.0 (poor match) and 1.0+ (excellent match with bonuses).
        /// </summary>
        private static float ScoreCellSizeForDimension(
            int[] transparencyCount,
            int dimension,
            int crossDimension,
            int cellSize
        )
        {
            if (cellSize <= 0 || dimension % cellSize != 0)
            {
                return 0f;
            }

            int cellCount = dimension / cellSize;
            if (cellCount < 1)
            {
                return 0f;
            }

            if (cellCount == 1)
            {
                return 0f;
            }

            int boundaryCount = cellCount - 1;
            float totalScore = 0f;

            for (int i = 1; i < cellCount; ++i)
            {
                int boundaryPosition = i * cellSize;

                if (boundaryPosition >= dimension)
                {
                    continue;
                }

                float boundaryScore = CalculateBoundaryTransparencyScore(
                    transparencyCount,
                    dimension,
                    crossDimension,
                    boundaryPosition
                );

                totalScore += boundaryScore;
            }

            return totalScore / boundaryCount;
        }

        /// <summary>
        /// Calculates a transparency score for a specific boundary position.
        /// Considers both the exact position and nearby positions (within 3 pixels)
        /// to handle slight misalignment in sprite sheets and thin transparent gutters.
        /// </summary>
        private static float CalculateBoundaryTransparencyScore(
            int[] transparencyCount,
            int dimension,
            int crossDimension,
            int position
        )
        {
            if (position < 0 || position >= dimension)
            {
                return 0f;
            }

            float primaryScore = transparencyCount[position] / (float)crossDimension;

            // Track max and average transparency in a 3-pixel radius
            float maxScore = primaryScore;
            float sumNearby = primaryScore;
            int nearbyCount = 1;

            // Expanded from +-2 to +-3 pixels to better detect thin transparent gutters
            for (int offset = -3; offset <= 3; ++offset)
            {
                if (offset == 0)
                {
                    continue;
                }

                int adjacentPos = position + offset;
                if (adjacentPos >= 0 && adjacentPos < dimension)
                {
                    float adjacentScore = transparencyCount[adjacentPos] / (float)crossDimension;
                    if (adjacentScore > maxScore)
                    {
                        maxScore = adjacentScore;
                    }
                    sumNearby += adjacentScore;
                    ++nearbyCount;
                }
            }

            float avgNearby = sumNearby / nearbyCount;

            // Weight max found transparency more heavily to detect thin gutters
            // maxScore helps when transparency is offset by a pixel
            // avgNearby helps when there's a wider transparent region
            // primaryScore gives slight preference to exact boundary position
            return maxScore * 0.6f + avgNearby * 0.25f + primaryScore * 0.15f;
        }

        /// <summary>
        /// Checks if a value is a power of two.
        /// </summary>
        private static bool IsPowerOfTwo(int value)
        {
            return value > 0 && (value & (value - 1)) == 0;
        }

        /// <summary>
        /// Finds the nearest divisor of a dimension to a target value that produces at least minCells cells.
        /// </summary>
        /// <param name="dimension">The texture dimension to find a divisor for.</param>
        /// <param name="target">The target cell size to find the nearest divisor to.</param>
        /// <param name="minCells">The minimum number of cells the divisor must produce.</param>
        /// <returns>The nearest divisor that produces at least minCells cells, or target if none found.</returns>
        internal static int FindNearestDivisorWithMinCells(int dimension, int target, int minCells)
        {
            if (dimension <= 0 || target <= 0 || minCells <= 0)
            {
                return target;
            }

            int bestDivisor = target;
            int bestDiff = int.MaxValue;
            bool found = false;

            for (int div = 1; div <= dimension; ++div)
            {
                if (dimension % div != 0)
                {
                    continue;
                }

                int cellCount = dimension / div;
                if (cellCount < minCells)
                {
                    continue;
                }

                int diff = Math.Abs(div - target);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    bestDivisor = div;
                    found = true;
                }
            }

            return found ? bestDivisor : target;
        }

        /// <summary>
        /// Detects cell size by analyzing opaque regions using flood-fill.
        /// Identifies individual sprite regions, computes their median dimensions,
        /// and finds the nearest divisor that produces multiple cells.
        /// </summary>
        /// <param name="pixels">The texture pixel data in Color32 format.</param>
        /// <param name="textureWidth">Width of the texture in pixels.</param>
        /// <param name="textureHeight">Height of the texture in pixels.</param>
        /// <param name="alphaThreshold">Alpha value (0-1) below which a pixel is considered transparent.</param>
        /// <returns>A tuple of (cellWidth, cellHeight), or (0, 0) if detection failed.</returns>
        internal static (int cellWidth, int cellHeight) DetectCellSizeFromOpaqueRegions(
            Color32[] pixels,
            int textureWidth,
            int textureHeight,
            float alphaThreshold
        )
        {
            if (pixels == null || pixels.Length == 0)
            {
                return (0, 0);
            }

            if (
                textureWidth < SpriteSheetAlgorithms.MinimumCellSize
                || textureHeight < SpriteSheetAlgorithms.MinimumCellSize
            )
            {
                return (0, 0);
            }

            if (pixels.Length != textureWidth * textureHeight)
            {
                return (0, 0);
            }

            byte alphaThresholdByte = (byte)(alphaThreshold * 255f);

            using PooledArray<bool> visitedLease = SystemArrayPool<bool>.Get(
                pixels.Length,
                out bool[] visited
            );
            Array.Clear(visited, 0, visited.Length);

            using PooledResource<List<int>> widthsLease = Buffers<int>.List.Get(
                out List<int> regionWidths
            );
            using PooledResource<List<int>> heightsLease = Buffers<int>.List.Get(
                out List<int> regionHeights
            );
            using PooledResource<List<int>> stackLease = Buffers<int>.List.Get(out List<int> stack);

            const int minimumRegionSize = SpriteSheetAlgorithms.MinimumCellSize;
            const int maxRegionCount = 256;

            for (int startY = 0; startY < textureHeight; ++startY)
            {
                if (regionWidths.Count >= maxRegionCount)
                {
                    break;
                }

                for (int startX = 0; startX < textureWidth; ++startX)
                {
                    int startIdx = startY * textureWidth + startX;
                    if (visited[startIdx])
                    {
                        continue;
                    }

                    if (pixels[startIdx].a <= alphaThresholdByte)
                    {
                        visited[startIdx] = true;
                        continue;
                    }

                    int minX = startX;
                    int maxX = startX;
                    int minY = startY;
                    int maxY = startY;

                    stack.Clear();
                    stack.Add(startIdx);
                    visited[startIdx] = true;

                    while (stack.Count > 0)
                    {
                        int lastIndex = stack.Count - 1;
                        int idx = stack[lastIndex];
                        stack.RemoveAt(lastIndex);

                        int px = idx % textureWidth;
                        int py = idx / textureWidth;

                        if (px < minX)
                        {
                            minX = px;
                        }
                        if (px > maxX)
                        {
                            maxX = px;
                        }
                        if (py < minY)
                        {
                            minY = py;
                        }
                        if (py > maxY)
                        {
                            maxY = py;
                        }

                        // 4-connected neighbors (up, down, left, right)
                        if (px > 0)
                        {
                            int left = idx - 1;
                            if (!visited[left] && pixels[left].a > alphaThresholdByte)
                            {
                                visited[left] = true;
                                stack.Add(left);
                            }
                        }
                        if (px < textureWidth - 1)
                        {
                            int right = idx + 1;
                            if (!visited[right] && pixels[right].a > alphaThresholdByte)
                            {
                                visited[right] = true;
                                stack.Add(right);
                            }
                        }
                        if (py > 0)
                        {
                            int down = idx - textureWidth;
                            if (!visited[down] && pixels[down].a > alphaThresholdByte)
                            {
                                visited[down] = true;
                                stack.Add(down);
                            }
                        }
                        if (py < textureHeight - 1)
                        {
                            int up = idx + textureWidth;
                            if (!visited[up] && pixels[up].a > alphaThresholdByte)
                            {
                                visited[up] = true;
                                stack.Add(up);
                            }
                        }
                    }

                    int regionWidth = maxX - minX + 1;
                    int regionHeight = maxY - minY + 1;

                    // Only include regions at least 4x4 pixels
                    if (regionWidth >= minimumRegionSize && regionHeight >= minimumRegionSize)
                    {
                        regionWidths.Add(regionWidth);
                        regionHeights.Add(regionHeight);
                    }
                }
            }

            if (regionWidths.Count < 2)
            {
                return (0, 0);
            }

            // Sort to find median
            regionWidths.Sort();
            regionHeights.Sort();

            int medianWidth = regionWidths[regionWidths.Count / 2];
            int medianHeight = regionHeights[regionHeights.Count / 2];

            // Find nearest divisors that produce at least 2 cells
            int cellWidth = FindNearestDivisorWithMinCells(textureWidth, medianWidth, 2);
            int cellHeight = FindNearestDivisorWithMinCells(textureHeight, medianHeight, 2);

            if (cellWidth < minimumRegionSize || cellHeight < minimumRegionSize)
            {
                return (0, 0);
            }

            return (cellWidth, cellHeight);
        }

        /// <summary>
        /// Verifies that grid lines do not cut through opaque sprite content.
        /// Checks both vertical and horizontal grid boundaries for excessive opaque pixels.
        /// </summary>
        /// <param name="pixels">The texture pixel data in Color32 format.</param>
        /// <param name="textureWidth">Width of the texture in pixels.</param>
        /// <param name="textureHeight">Height of the texture in pixels.</param>
        /// <param name="cellWidth">The cell width to verify.</param>
        /// <param name="cellHeight">The cell height to verify.</param>
        /// <param name="alphaThreshold">Alpha value (0-1) below which a pixel is considered transparent.</param>
        /// <returns>True if the grid is valid (does not cut sprites), false if more than 30% of grid line pixels are opaque.</returns>
        internal static bool VerifyGridDoesNotCutSprites(
            Color32[] pixels,
            int textureWidth,
            int textureHeight,
            int cellWidth,
            int cellHeight,
            float alphaThreshold
        )
        {
            if (pixels == null || pixels.Length == 0)
            {
                return true;
            }

            if (cellWidth <= 0 || cellHeight <= 0)
            {
                return true;
            }

            if (pixels.Length != textureWidth * textureHeight)
            {
                return true;
            }

            byte alphaThresholdByte = (byte)(alphaThreshold * 255f);
            int opaqueOnGridLines = 0;
            int totalGridLinePixels = 0;

            // Check vertical grid lines (at each column boundary)
            for (int col = 1; col < textureWidth / cellWidth; ++col)
            {
                int x = col * cellWidth;
                if (x >= textureWidth)
                {
                    break;
                }

                for (int y = 0; y < textureHeight; ++y)
                {
                    int idx = y * textureWidth + x;
                    ++totalGridLinePixels;
                    if (pixels[idx].a > alphaThresholdByte)
                    {
                        ++opaqueOnGridLines;
                    }
                }
            }

            // Check horizontal grid lines (at each row boundary)
            for (int row = 1; row < textureHeight / cellHeight; ++row)
            {
                int y = row * cellHeight;
                if (y >= textureHeight)
                {
                    break;
                }

                for (int x = 0; x < textureWidth; ++x)
                {
                    int idx = y * textureWidth + x;
                    ++totalGridLinePixels;
                    if (pixels[idx].a > alphaThresholdByte)
                    {
                        ++opaqueOnGridLines;
                    }
                }
            }

            if (totalGridLinePixels == 0)
            {
                return true;
            }

            float opaqueRatio = (float)opaqueOnGridLines / totalGridLinePixels;

            // Return false if 30% or more of grid line pixels are opaque
            return opaqueRatio < 0.3f;
        }

        /// <summary>
        /// Fallback method that finds consistent spacing using the legacy approach.
        /// First identifies highly transparent boundaries, then looks for consistent gaps.
        /// </summary>
        private static int FindConsistentSpacingFromTransparency(
            int[] transparencyCount,
            int dimension,
            int crossDimension,
            int minimumCellSize
        )
        {
            // Lowered from 90% to 70% to handle sprite sheets with partial transparency
            float transparencyRequirement = 0.70f;
            int minTransparentPixels = (int)(crossDimension * transparencyRequirement);

            using PooledResource<List<int>> boundariesLease = Buffers<int>.List.Get(
                out List<int> boundaries
            );

            for (int i = 0; i < dimension; ++i)
            {
                if (transparencyCount[i] >= minTransparentPixels)
                {
                    boundaries.Add(i);
                }
            }

            // If insufficient boundaries found with 70%, try again with 50%
            if (boundaries.Count < 2)
            {
                transparencyRequirement = 0.50f;
                minTransparentPixels = (int)(crossDimension * transparencyRequirement);
                boundaries.Clear();

                for (int i = 0; i < dimension; ++i)
                {
                    if (transparencyCount[i] >= minTransparentPixels)
                    {
                        boundaries.Add(i);
                    }
                }
            }

            if (boundaries.Count < 2)
            {
                return 0;
            }

            return FindConsistentSpacing(boundaries, dimension, minimumCellSize);
        }

        /// <summary>
        /// Finds consistent spacing between transparent boundaries.
        /// Returns the most common spacing if it divides the dimension evenly.
        /// Uses a tolerance of +/- 2 pixels when searching for valid divisors.
        /// </summary>
        private static int FindConsistentSpacing(
            List<int> boundaries,
            int totalDimension,
            int minimumCellSize
        )
        {
            if (boundaries.Count < 2)
            {
                return 0;
            }

            using PooledResource<List<int>> gapsLease = Buffers<int>.List.Get(out List<int> gaps);

            int firstBoundary = boundaries[0];
            if (firstBoundary > 0 && firstBoundary >= minimumCellSize)
            {
                gaps.Add(firstBoundary);
            }

            for (int i = 1; i < boundaries.Count; ++i)
            {
                int gap = boundaries[i] - boundaries[i - 1];
                if (gap >= minimumCellSize)
                {
                    gaps.Add(gap);
                }
            }

            int lastBoundary = boundaries[boundaries.Count - 1];
            int trailingGap = totalDimension - lastBoundary - 1;
            if (trailingGap >= minimumCellSize)
            {
                gaps.Add(trailingGap);
            }

            if (gaps.Count < 2)
            {
                return 0;
            }

            using PooledResource<Dictionary<int, int>> gapCountsLease = DictionaryBuffer<
                int,
                int
            >.Dictionary.Get(out Dictionary<int, int> gapCounts);

            for (int i = 0; i < gaps.Count; ++i)
            {
                int gap = gaps[i];
                if (gapCounts.TryGetValue(gap, out int count))
                {
                    gapCounts[gap] = count + 1;
                }
                else
                {
                    gapCounts[gap] = 1;
                }
            }

            int mostCommonGap = 0;
            int mostCommonCount = 0;

            using PooledResource<List<int>> gapKeysLease = Buffers<int>.List.Get(
                out List<int> gapKeys
            );
            using (Dictionary<int, int>.Enumerator enumerator = gapCounts.GetEnumerator())
            {
                while (enumerator.MoveNext())
                {
                    gapKeys.Add(enumerator.Current.Key);
                }
            }

            for (int i = 0; i < gapKeys.Count; ++i)
            {
                int gap = gapKeys[i];
                int count = gapCounts[gap];
                if (count > mostCommonCount)
                {
                    mostCommonCount = count;
                    mostCommonGap = gap;
                }
            }

            if (mostCommonGap > 0 && totalDimension % mostCommonGap == 0)
            {
                int expectedColumns = totalDimension / mostCommonGap;
                if (expectedColumns >= 2 && mostCommonCount >= expectedColumns - 1)
                {
                    return mostCommonGap;
                }
            }

            for (int tolerance = 1; tolerance <= 2; ++tolerance)
            {
                int candidateLow = mostCommonGap - tolerance;
                if (candidateLow >= minimumCellSize && totalDimension % candidateLow == 0)
                {
                    int expectedColumns = totalDimension / candidateLow;
                    if (expectedColumns >= 2)
                    {
                        return candidateLow;
                    }
                }

                int candidateHigh = mostCommonGap + tolerance;
                if (candidateHigh >= minimumCellSize && totalDimension % candidateHigh == 0)
                {
                    int expectedColumns = totalDimension / candidateHigh;
                    if (expectedColumns >= 2)
                    {
                        return candidateHigh;
                    }
                }
            }

            return 0;
        }

        internal ExtractionMode GetEffectiveExtractionMode(SpriteSheetEntry entry)
        {
            ExtractionMode mode;
            if (
                entry == null
                || entry._useGlobalSettings
                || !entry._extractionModeOverride.HasValue
            )
            {
                mode = _extractionMode;
            }
            else
            {
                mode = entry._extractionModeOverride.Value;
            }

#pragma warning disable CS0618 // Type or member is obsolete
            if (mode == ExtractionMode.None)
            {
                return ExtractionMode.FromMetadata;
            }
#pragma warning restore CS0618

            return mode;
        }

        internal GridSizeMode GetEffectiveGridSizeMode(SpriteSheetEntry entry)
        {
            GridSizeMode mode;
            if (entry == null || entry._useGlobalSettings || !entry._gridSizeModeOverride.HasValue)
            {
                mode = _gridSizeMode;
            }
            else
            {
                mode = entry._gridSizeModeOverride.Value;
            }

#pragma warning disable CS0618 // Type or member is obsolete
            if (mode == GridSizeMode.None)
            {
                return GridSizeMode.Auto;
            }
#pragma warning restore CS0618

            return mode;
        }

        internal int GetEffectiveGridColumns(SpriteSheetEntry entry)
        {
            if (entry == null || entry._useGlobalSettings || !entry._gridColumnsOverride.HasValue)
            {
                return _gridColumns;
            }
            return entry._gridColumnsOverride.Value;
        }

        internal int GetEffectiveGridRows(SpriteSheetEntry entry)
        {
            if (entry == null || entry._useGlobalSettings || !entry._gridRowsOverride.HasValue)
            {
                return _gridRows;
            }
            return entry._gridRowsOverride.Value;
        }

        internal int GetEffectiveCellWidth(SpriteSheetEntry entry)
        {
            if (entry == null || entry._useGlobalSettings || !entry._cellWidthOverride.HasValue)
            {
                return _cellWidth;
            }
            return entry._cellWidthOverride.Value;
        }

        internal int GetEffectiveCellHeight(SpriteSheetEntry entry)
        {
            if (entry == null || entry._useGlobalSettings || !entry._cellHeightOverride.HasValue)
            {
                return _cellHeight;
            }
            return entry._cellHeightOverride.Value;
        }

        internal int GetEffectivePaddingLeft(SpriteSheetEntry entry)
        {
            if (entry == null || entry._useGlobalSettings || !entry._paddingLeftOverride.HasValue)
            {
                return _paddingLeft;
            }
            return entry._paddingLeftOverride.Value;
        }

        internal int GetEffectivePaddingRight(SpriteSheetEntry entry)
        {
            if (entry == null || entry._useGlobalSettings || !entry._paddingRightOverride.HasValue)
            {
                return _paddingRight;
            }
            return entry._paddingRightOverride.Value;
        }

        internal int GetEffectivePaddingTop(SpriteSheetEntry entry)
        {
            if (entry == null || entry._useGlobalSettings || !entry._paddingTopOverride.HasValue)
            {
                return _paddingTop;
            }
            return entry._paddingTopOverride.Value;
        }

        internal int GetEffectivePaddingBottom(SpriteSheetEntry entry)
        {
            if (entry == null || entry._useGlobalSettings || !entry._paddingBottomOverride.HasValue)
            {
                return _paddingBottom;
            }
            return entry._paddingBottomOverride.Value;
        }

        internal float GetEffectiveAlphaThreshold(SpriteSheetEntry entry)
        {
            if (
                entry == null
                || entry._useGlobalSettings
                || !entry._alphaThresholdOverride.HasValue
            )
            {
                return _alphaThreshold;
            }
            return entry._alphaThresholdOverride.Value;
        }

        /// <summary>
        /// Returns whether the overlay should be shown for the given entry.
        /// Uses per-sheet override if set, otherwise falls back to global setting.
        /// </summary>
        /// <param name="entry">The sprite sheet entry to check, or null to use global setting.</param>
        /// <returns>True if the overlay should be displayed for this entry.</returns>
        internal bool GetEffectiveShowOverlay(SpriteSheetEntry entry)
        {
            if (entry == null || entry._useGlobalSettings || !entry._showOverlayOverride.HasValue)
            {
                return _showOverlay;
            }
            return entry._showOverlayOverride.Value;
        }

        /// <summary>
        /// Returns the effective pivot mode for the given entry.
        /// Uses per-sheet override if set, otherwise falls back to global setting.
        /// </summary>
        /// <param name="entry">The sprite sheet entry to check, or null to use global setting.</param>
        /// <returns>The pivot mode to use for this entry.</returns>
        internal PivotMode GetEffectivePivotMode(SpriteSheetEntry entry)
        {
            if (entry == null || entry._useGlobalSettings || !entry._pivotModeOverride.HasValue)
            {
                return _pivotMode;
            }
            return entry._pivotModeOverride.Value;
        }

        /// <summary>
        /// Returns the effective custom pivot for the given entry.
        /// Uses per-sheet override if set, otherwise falls back to global setting.
        /// </summary>
        /// <param name="entry">The sprite sheet entry to check, or null to use global setting.</param>
        /// <returns>The custom pivot point to use for this entry.</returns>
        internal Vector2 GetEffectiveCustomPivot(SpriteSheetEntry entry)
        {
            if (entry == null || entry._useGlobalSettings || !entry._customPivotOverride.HasValue)
            {
                return _customPivot;
            }
            return entry._customPivotOverride.Value;
        }

        /// <summary>
        /// Returns the effective auto-detection algorithm for the given entry.
        /// Uses per-sheet override if set, otherwise falls back to global setting.
        /// </summary>
        /// <param name="entry">The sprite sheet entry to check, or null to use global setting.</param>
        /// <returns>The algorithm to use for this entry.</returns>
        internal AutoDetectionAlgorithm GetEffectiveAutoDetectionAlgorithm(SpriteSheetEntry entry)
        {
            if (
                entry == null
                || entry._useGlobalSettings
                || !entry._autoDetectionAlgorithmOverride.HasValue
            )
            {
                return _autoDetectionAlgorithm;
            }
            return entry._autoDetectionAlgorithmOverride.Value;
        }

        /// <summary>
        /// Returns the effective expected sprite count for the given entry.
        /// Uses per-sheet override if set, otherwise falls back to global setting.
        /// </summary>
        /// <param name="entry">The sprite sheet entry to check, or null to use global setting.</param>
        /// <returns>The expected sprite count, or -1 if not set.</returns>
        internal int GetEffectiveExpectedSpriteCount(SpriteSheetEntry entry)
        {
            if (
                entry == null
                || entry._useGlobalSettings
                || !entry._expectedSpriteCountOverride.HasValue
            )
            {
                return _expectedSpriteCountHint;
            }
            return entry._expectedSpriteCountOverride.Value;
        }

        /// <summary>
        /// Returns the effective snap to texture divisor setting for the given entry.
        /// Uses per-sheet override if set, otherwise falls back to global setting.
        /// </summary>
        /// <param name="entry">The sprite sheet entry to check, or null to use global setting.</param>
        /// <returns>True if cell sizes should be adjusted to be exact divisors of texture dimensions.</returns>
        internal bool GetEffectiveSnapToTextureDivisor(SpriteSheetEntry entry)
        {
            if (
                entry == null
                || entry._useGlobalSettings
                || !entry._snapToTextureDivisorOverride.HasValue
            )
            {
                return _snapToTextureDivisor;
            }
            return entry._snapToTextureDivisorOverride.Value;
        }

        /// <summary>
        /// Gets the effective pivot marker color for a sprite, respecting the cascade:
        /// per-element, per-sheet, global.
        /// </summary>
        /// <param name="entry">The sprite sheet entry containing the sprite.</param>
        /// <param name="sprite">The individual sprite entry to get the color for.</param>
        /// <returns>The effective pivot marker color for the sprite.</returns>
        internal Color GetEffectivePivotColor(SpriteSheetEntry entry, SpriteEntryData sprite)
        {
            if (sprite != null && sprite._usePivotColorOverride)
            {
                return sprite._pivotColorOverride;
            }

            if (entry != null && entry._usePivotMarkerColorOverride)
            {
                return entry._pivotMarkerColorOverride;
            }

            return _pivotMarkerColor;
        }

        /// <summary>
        /// Gets the effective pivot position for a sprite, respecting the cascade:
        /// per-element override, per-sheet override, global settings.
        /// </summary>
        /// <param name="entry">The sprite sheet entry containing the sprite.</param>
        /// <param name="sprite">The individual sprite entry to get the pivot for.</param>
        /// <returns>
        /// The effective pivot as a normalized Vector2 where (0,0) is bottom-left and (1,1) is top-right.
        /// </returns>
        internal Vector2 GetEffectivePivot(SpriteSheetEntry entry, SpriteEntryData sprite)
        {
            if (sprite != null && sprite._usePivotOverride)
            {
                return PivotModeToVector2(sprite._pivotModeOverride, sprite._customPivotOverride);
            }

            if (entry != null && !entry._useGlobalSettings && entry._pivotModeOverride.HasValue)
            {
                Vector2 customPivot = entry._customPivotOverride ?? _customPivot;
                return PivotModeToVector2(entry._pivotModeOverride.Value, customPivot);
            }

            return PivotModeToVector2(_pivotMode, _customPivot);
        }

        /// <summary>
        /// Initializes all override fields from their corresponding global values.
        /// Called when transitioning from global settings to per-sheet settings.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This method is called AFTER <paramref name="entry"/>._useGlobalSettings has been set to false.
        /// It copies all current effective values (which are still the global values) to the override fields,
        /// ensuring the UI toggle states match the actual rendering behavior.
        /// </para>
        /// <para>
        /// The method intentionally does NOT modify _useGlobalSettings since the caller has already changed it.
        /// </para>
        /// </remarks>
        /// <param name="entry">The sprite sheet entry to initialize. If null, the method returns immediately.</param>
        internal void InitializeOverridesFromGlobal(SpriteSheetEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            // Copy all current effective values (global settings) to override fields.
            // After this, the entry can be switched back to _useGlobalSettings=true
            // without losing the customized values.
            entry._extractionModeOverride = GetEffectiveExtractionMode(entry);
            entry._gridSizeModeOverride = GetEffectiveGridSizeMode(entry);
            entry._gridColumnsOverride = GetEffectiveGridColumns(entry);
            entry._gridRowsOverride = GetEffectiveGridRows(entry);
            entry._cellWidthOverride = GetEffectiveCellWidth(entry);
            entry._cellHeightOverride = GetEffectiveCellHeight(entry);
            entry._paddingLeftOverride = GetEffectivePaddingLeft(entry);
            entry._paddingRightOverride = GetEffectivePaddingRight(entry);
            entry._paddingTopOverride = GetEffectivePaddingTop(entry);
            entry._paddingBottomOverride = GetEffectivePaddingBottom(entry);
            entry._alphaThresholdOverride = GetEffectiveAlphaThreshold(entry);
            entry._showOverlayOverride = GetEffectiveShowOverlay(entry);
            entry._pivotModeOverride = GetEffectivePivotMode(entry);
            entry._customPivotOverride = GetEffectiveCustomPivot(entry);
            entry._autoDetectionAlgorithmOverride = GetEffectiveAutoDetectionAlgorithm(entry);
            entry._expectedSpriteCountOverride = GetEffectiveExpectedSpriteCount(entry);
            entry._snapToTextureDivisorOverride = GetEffectiveSnapToTextureDivisor(entry);
            entry._usePivotMarkerColorOverride = false;
            entry._pivotMarkerColorOverride = _pivotMarkerColor;

            // Clear cached algorithm result to force recalculation with new settings
            entry._cachedAlgorithmResult = null;
            entry._lastAlgorithmDisplayText = null;
        }

        /// <summary>
        /// Converts a PivotMode enum value to the corresponding normalized Vector2 pivot coordinates.
        /// </summary>
        /// <param name="pivotMode">The pivot mode to convert.</param>
        /// <param name="customPivot">The custom pivot to use when pivotMode is Custom.</param>
        /// <returns>Normalized pivot coordinates where (0,0) is bottom-left and (1,1) is top-right.</returns>
        internal static Vector2 PivotModeToVector2(PivotMode pivotMode, Vector2 customPivot)
        {
            return pivotMode switch
            {
                PivotMode.Center => new Vector2(0.5f, 0.5f),
                PivotMode.BottomLeft => new Vector2(0f, 0f),
                PivotMode.TopLeft => new Vector2(0f, 1f),
                PivotMode.BottomRight => new Vector2(1f, 0f),
                PivotMode.TopRight => new Vector2(1f, 1f),
                PivotMode.LeftCenter => new Vector2(0f, 0.5f),
                PivotMode.RightCenter => new Vector2(1f, 0.5f),
                PivotMode.TopCenter => new Vector2(0.5f, 1f),
                PivotMode.BottomCenter => new Vector2(0.5f, 0f),
                PivotMode.Custom => customPivot,
                _ => new Vector2(0.5f, 0.5f),
            };
        }

        /// <summary>
        /// Converts a sprite rect (in texture coordinates) to screen coordinates within the preview rect.
        /// </summary>
        /// <param name="textureRect">The texture-space rect within the preview.</param>
        /// <param name="spriteRect">The sprite rect in texture coordinates.</param>
        /// <param name="textureHeight">The height of the source texture.</param>
        /// <param name="scale">The scale factor from texture to screen coordinates.</param>
        /// <returns>The screen-space rect corresponding to the sprite.</returns>
        internal static Rect ConvertTextureRectToScreenRect(
            Rect textureRect,
            Rect spriteRect,
            int textureHeight,
            float scale
        )
        {
            float screenX = textureRect.x + spriteRect.x * scale;
            float screenY =
                textureRect.y + (textureHeight - spriteRect.y - spriteRect.height) * scale;
            float screenWidth = spriteRect.width * scale;
            float screenHeight = spriteRect.height * scale;
            return new Rect(screenX, screenY, screenWidth, screenHeight);
        }

        /// <summary>
        /// Calculates the normalized pivot position within a sprite based on a screen position.
        /// </summary>
        /// <param name="screenPosition">The mouse position in screen coordinates.</param>
        /// <param name="spriteScreenRect">The sprite rect in screen coordinates.</param>
        /// <returns>Normalized position (0-1) where (0,0) is bottom-left and (1,1) is top-right.</returns>
        internal static Vector2 CalculateNormalizedPositionInSprite(
            Vector2 screenPosition,
            Rect spriteScreenRect
        )
        {
            float normalizedX = (screenPosition.x - spriteScreenRect.x) / spriteScreenRect.width;
            float normalizedY =
                1f - (screenPosition.y - spriteScreenRect.y) / spriteScreenRect.height;
            return new Vector2(Mathf.Clamp01(normalizedX), Mathf.Clamp01(normalizedY));
        }

        /// <summary>
        /// Finds which sprite (if any) contains the given screen position.
        /// </summary>
        /// <param name="screenPosition">The mouse position in screen coordinates.</param>
        /// <param name="textureRect">The texture-space rect within the preview.</param>
        /// <param name="entry">The sprite sheet entry containing the sprites.</param>
        /// <param name="textureHeight">The height of the source texture.</param>
        /// <param name="scale">The scale factor from texture to screen coordinates.</param>
        /// <returns>The index of the sprite containing the position, or -1 if none.</returns>
        internal static int FindSpriteAtScreenPosition(
            Vector2 screenPosition,
            Rect textureRect,
            SpriteSheetEntry entry,
            int textureHeight,
            float scale
        )
        {
            if (entry == null || entry._sprites == null)
            {
                return -1;
            }

            for (int i = 0; i < entry._sprites.Count; ++i)
            {
                SpriteEntryData sprite = entry._sprites[i];
                if (sprite == null)
                {
                    continue;
                }

                Rect screenRect = ConvertTextureRectToScreenRect(
                    textureRect,
                    sprite._rect,
                    textureHeight,
                    scale
                );

                if (screenRect.Contains(screenPosition))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Computes the SHA256 hash of a file's contents.
        /// </summary>
        /// <param name="filePath">The path to the file to hash.</param>
        /// <returns>The SHA256 hash as a lowercase hex string, or null if the file cannot be read.</returns>
        internal static string ComputeFileHash(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            try
            {
                using SHA256 sha256 = SHA256.Create();
                using FileStream stream = File.OpenRead(filePath);
                byte[] hashBytes = sha256.ComputeHash(stream);
                StringBuilder builder = new(hashBytes.Length * 2);
                for (int i = 0; i < hashBytes.Length; ++i)
                {
                    _ = builder.Append(hashBytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Saves the configuration for a sprite sheet entry to a JSON file.
        /// </summary>
        /// <param name="entry">The entry to save configuration for.</param>
        /// <returns>True if the config was saved successfully, false otherwise.</returns>
        internal bool SaveConfig(SpriteSheetEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry._assetPath))
            {
                return false;
            }

            try
            {
                string configPath = SpriteSheetConfig.GetConfigPath(entry._assetPath);
                string fullConfigPath = Path.GetFullPath(configPath);

                string textureFullPath = Path.GetFullPath(entry._assetPath);
                string hash = ComputeFileHash(textureFullPath);

                CachedAlgorithmResult cachedResult = null;
                if (
                    entry._cachedAlgorithmResult.HasValue
                    && entry._cachedAlgorithmResult.Value.IsValid
                )
                {
                    cachedResult = CachedAlgorithmResult.FromResult(
                        entry._cachedAlgorithmResult.Value
                    );
                }

                SpriteSheetConfig config = new()
                {
                    version = SpriteSheetConfig.CurrentVersion,
                    pivotMode = GetEffectivePivotMode(entry),
                    customPivot = GetEffectiveCustomPivot(entry),
                    algorithm = (int)GetEffectiveAutoDetectionAlgorithm(entry),
                    expectedSpriteCount = GetEffectiveExpectedSpriteCount(entry),
                    textureContentHash = hash,
                    cachedAlgorithmResult = cachedResult,
                    snapToTextureDivisor = GetEffectiveSnapToTextureDivisor(entry),
                };

                string json = Serializer.JsonStringify(config, pretty: true);
                File.WriteAllText(fullConfigPath, json, Encoding.UTF8);

                entry._loadedConfig = config;
                entry._configLoaded = true;
                entry._configStale = false;

                AssetDatabase.Refresh();
                return true;
            }
            catch (Exception e)
            {
                this.LogError($"Failed to save config for '{entry._assetPath}'", e);
                return false;
            }
        }

        /// <summary>
        /// Loads the configuration for a sprite sheet entry from a JSON file.
        /// </summary>
        /// <param name="entry">The entry to load configuration for.</param>
        /// <returns>True if the config was loaded successfully, false otherwise.</returns>
        internal bool LoadConfig(SpriteSheetEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry._assetPath))
            {
                return false;
            }

            try
            {
                string configPath = SpriteSheetConfig.GetConfigPath(entry._assetPath);
                string fullConfigPath = Path.GetFullPath(configPath);

                if (!File.Exists(fullConfigPath))
                {
                    entry._configLoaded = false;
                    entry._configStale = false;
                    entry._loadedConfig = null;
                    return false;
                }

                string json = File.ReadAllText(fullConfigPath, Encoding.UTF8);
                SpriteSheetConfig config = Serializer.JsonDeserialize<SpriteSheetConfig>(json);

                if (config == null)
                {
                    entry._configLoaded = false;
                    entry._configStale = false;
                    entry._loadedConfig = null;
                    return false;
                }

                SpriteSheetConfig.MigrateConfig(config);

                string textureFullPath = Path.GetFullPath(entry._assetPath);
                string currentHash = ComputeFileHash(textureFullPath);
                bool isStale =
                    !string.IsNullOrEmpty(config.textureContentHash)
                    && !string.Equals(
                        config.textureContentHash,
                        currentHash,
                        StringComparison.OrdinalIgnoreCase
                    );

                entry._loadedConfig = config;
                entry._configLoaded = true;
                entry._configStale = isStale;

                entry._pivotModeOverride = config.pivotMode;
                entry._customPivotOverride = config.customPivot;
                entry._autoDetectionAlgorithmOverride = (AutoDetectionAlgorithm)config.algorithm;
                entry._expectedSpriteCountOverride = config.expectedSpriteCount;
                entry._snapToTextureDivisorOverride = config.snapToTextureDivisor;
                // IMPORTANT: Do NOT restore cachedAlgorithmResult from config.
                // The cached result may have been computed with different settings (like a different
                // expectedSpriteCount), and restoring it can cause stale results to be used.
                // The algorithm will re-run and cache fresh results as needed.
                entry._cachedAlgorithmResult = null;
                entry._useGlobalSettings = false;

                return true;
            }
            catch (Exception e)
            {
                this.LogError($"Failed to load config for '{entry._assetPath}'", e);
                entry._configLoaded = false;
                entry._configStale = false;
                entry._loadedConfig = null;
                return false;
            }
        }

        /// <summary>
        /// Attempts to auto-load config for an entry if the config file exists.
        /// </summary>
        /// <param name="entry">The entry to auto-load config for.</param>
        internal void TryAutoLoadConfig(SpriteSheetEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry._assetPath))
            {
                return;
            }

            string configPath = SpriteSheetConfig.GetConfigPath(entry._assetPath);
            string fullConfigPath = Path.GetFullPath(configPath);

            if (File.Exists(fullConfigPath))
            {
                _ = LoadConfig(entry);
            }
        }

        /// <summary>
        /// Calculates the actual rect where the texture is drawn within the preview area.
        /// This accounts for ScaleMode.ScaleToFit centering behavior.
        /// </summary>
        internal Rect CalculateTextureRectWithinPreview(
            Rect previewRect,
            int textureWidth,
            int textureHeight,
            float scale
        )
        {
            // Defensive check for invalid inputs - fall back to full preview rect
            if (textureWidth <= 0 || textureHeight <= 0 || scale <= 0f)
            {
                return previewRect;
            }

            float scaledWidth = textureWidth * scale;
            float scaledHeight = textureHeight * scale;
            float offsetX = (previewRect.width - scaledWidth) * 0.5f;
            float offsetY = (previewRect.height - scaledHeight) * 0.5f;
            return new Rect(
                previewRect.x + offsetX,
                previewRect.y + offsetY,
                scaledWidth,
                scaledHeight
            );
        }

        /// <summary>
        /// Draws outline rectangles around each sprite bound in the source texture preview.
        /// This method replaces the old grid overlay with actual sprite bounds visualization.
        /// </summary>
        /// <param name="previewRect">The preview area rectangle.</param>
        /// <param name="textureWidth">Width of the source texture.</param>
        /// <param name="textureHeight">Height of the source texture.</param>
        /// <param name="scale">Scale factor for drawing.</param>
        /// <param name="entry">The sprite sheet entry containing sprite bounds.</param>
        internal void DrawSpriteBoundsOverlay(
            Rect previewRect,
            int textureWidth,
            int textureHeight,
            float scale,
            SpriteSheetEntry entry
        )
        {
            if (entry == null)
            {
                return;
            }

            CheckAndRegenerateIfNeeded(entry);

            if (entry._sprites == null || entry._sprites.Count == 0)
            {
                if (DiagnosticsEnabled)
                {
                    this.Log(
                        $"DrawSpriteBoundsOverlay: no sprites for '{entry._assetPath}', sprites={entry._sprites?.Count ?? 0}"
                    );
                }
                return;
            }

            if (DiagnosticsEnabled && entry._sprites.Count > 0)
            {
                SpriteEntryData firstSprite = entry._sprites[0];
                this.Log(
                    $"DrawSpriteBoundsOverlay: drawing {entry._sprites.Count} sprites for '{Path.GetFileName(entry._assetPath)}', firstRect={firstSprite._rect}, algorithm={entry._lastAlgorithmDisplayText}"
                );
            }

            Rect textureRect = CalculateTextureRectWithinPreview(
                previewRect,
                textureWidth,
                textureHeight,
                scale
            );

            for (int i = 0; i < entry._sprites.Count; ++i)
            {
                SpriteEntryData sprite = entry._sprites[i];
                if (sprite == null)
                {
                    continue;
                }

                float rectX = textureRect.x + sprite._rect.x * scale;
                float rectY =
                    textureRect.y + (textureHeight - sprite._rect.y - sprite._rect.height) * scale;
                float rectWidth = sprite._rect.width * scale;
                float rectHeight = sprite._rect.height * scale;

                EditorGUI.DrawRect(new Rect(rectX, rectY, rectWidth, 1), _overlayColor);
                EditorGUI.DrawRect(
                    new Rect(rectX, rectY + rectHeight - 1, rectWidth, 1),
                    _overlayColor
                );
                EditorGUI.DrawRect(new Rect(rectX, rectY, 1, rectHeight), _overlayColor);
                EditorGUI.DrawRect(
                    new Rect(rectX + rectWidth - 1, rectY, 1, rectHeight),
                    _overlayColor
                );

                Vector2 effectivePivot = GetEffectivePivot(entry, sprite);
                bool isNonDefaultPivot = effectivePivot != CenterPivot || sprite._usePivotOverride;

                if (isNonDefaultPivot)
                {
                    Color pivotColor = GetEffectivePivotColor(entry, sprite);
                    Rect screenRect = new Rect(rectX, rectY, rectWidth, rectHeight);
                    DrawPivotMarker(screenRect, effectivePivot, pivotColor);
                }
            }
        }

        /// <summary>
        /// Draws a grid overlay based on current grid settings when sprites haven't been generated yet.
        /// This provides visual feedback during regeneration or before sprites are populated.
        /// </summary>
        /// <param name="previewRect">The preview area rectangle.</param>
        /// <param name="textureWidth">Width of the source texture.</param>
        /// <param name="textureHeight">Height of the source texture.</param>
        /// <param name="scale">Scale factor for drawing.</param>
        /// <param name="entry">The sprite sheet entry to get grid settings from.</param>
        private void DrawGridOverlayFromSettings(
            Rect previewRect,
            int textureWidth,
            int textureHeight,
            float scale,
            SpriteSheetEntry entry
        )
        {
            if (entry == null || textureWidth <= 0 || textureHeight <= 0)
            {
                return;
            }

            Rect textureRect = CalculateTextureRectWithinPreview(
                previewRect,
                textureWidth,
                textureHeight,
                scale
            );

            int columns;
            int rows;
            int cellWidth;
            int cellHeight;

            // For Auto mode, try to use cached algorithm result or read texture pixels
            // to ensure the overlay reflects the algorithm-detected grid
            GridSizeMode effectiveGridSizeMode = GetEffectiveGridSizeMode(entry);
            if (effectiveGridSizeMode == GridSizeMode.Auto)
            {
                // First check for cached algorithm result
                if (
                    entry._cachedAlgorithmResult.HasValue
                    && entry._cachedAlgorithmResult.Value.IsValid
                )
                {
                    cellWidth = entry._cachedAlgorithmResult.Value.CellWidth;
                    cellHeight = entry._cachedAlgorithmResult.Value.CellHeight;
                    columns = textureWidth / cellWidth;
                    rows = textureHeight / cellHeight;
                }
                else if (entry._texture != null && entry._texture.isReadable)
                {
                    // No cached result - try to calculate with pixels for algorithm detection
                    Color32[] pixels = entry._texture.GetPixels32();
                    CalculateGridDimensions(
                        textureWidth,
                        textureHeight,
                        entry,
                        pixels,
                        out columns,
                        out rows,
                        out cellWidth,
                        out cellHeight
                    );
                }
                else
                {
                    // No cached result and texture not readable - cannot show accurate overlay
                    // The sprite population methods will make the texture readable and cache the result
                    // Don't draw anything rather than show inaccurate heuristic-based overlay
                    if (DiagnosticsEnabled)
                    {
                        this.Log(
                            $"DrawGridOverlayFromSettings: No cached algorithm result and texture not readable for '{entry._assetPath}'"
                        );
                    }
                    return;
                }
            }
            else
            {
                // Manual mode - calculate directly from settings
                CalculateGridDimensions(
                    textureWidth,
                    textureHeight,
                    entry,
                    out columns,
                    out rows,
                    out cellWidth,
                    out cellHeight
                );
            }

            if (columns <= 0 || rows <= 0 || cellWidth <= 0 || cellHeight <= 0)
            {
                return;
            }

            // Get padding values for positioning
            int paddingLeft = GetEffectivePaddingLeft(entry);
            int paddingBottom = GetEffectivePaddingBottom(entry);

            // Draw grid cells as rectangles
            for (int row = 0; row < rows; ++row)
            {
                for (int col = 0; col < columns; ++col)
                {
                    // Calculate sprite rect in texture space (bottom-left origin)
                    int spriteX = paddingLeft + col * cellWidth;
                    int spriteY = paddingBottom + row * cellHeight;

                    // Convert to screen coordinates (top-left origin)
                    float rectX = textureRect.x + spriteX * scale;
                    float rectY = textureRect.y + (textureHeight - spriteY - cellHeight) * scale;
                    float rectWidth = cellWidth * scale;
                    float rectHeight = cellHeight * scale;

                    // Draw rectangle outline (same style as DrawSpriteBoundsOverlay)
                    EditorGUI.DrawRect(new Rect(rectX, rectY, rectWidth, 1), _overlayColor);
                    EditorGUI.DrawRect(
                        new Rect(rectX, rectY + rectHeight - 1, rectWidth, 1),
                        _overlayColor
                    );
                    EditorGUI.DrawRect(new Rect(rectX, rectY, 1, rectHeight), _overlayColor);
                    EditorGUI.DrawRect(
                        new Rect(rectX + rectWidth - 1, rectY, 1, rectHeight),
                        _overlayColor
                    );
                }
            }
        }

        /// <summary>
        /// Draws a crosshair pivot marker at the specified pivot position within a sprite rect.
        /// </summary>
        /// <remarks>
        /// The pivot position is normalized where (0,0) is bottom-left and (1,1) is top-right.
        /// The Y coordinate is flipped for screen coordinates where (0,0) is top-left.
        /// </remarks>
        /// <param name="spriteRect">The screen-space rectangle of the sprite.</param>
        /// <param name="pivot">The normalized pivot position (0-1 range).</param>
        /// <param name="color">The color to draw the crosshair.</param>
        private void DrawPivotMarker(Rect spriteRect, Vector2 pivot, Color color)
        {
            DrawPivotMarker(spriteRect, pivot, color, false, false);
        }

        /// <summary>
        /// Draws a crosshair pivot marker with optional hover/drag feedback.
        /// </summary>
        /// <param name="spriteRect">The screen-space rectangle of the sprite.</param>
        /// <param name="pivot">The normalized pivot position (0-1 range).</param>
        /// <param name="color">The base color to draw the crosshair.</param>
        /// <param name="isHovering">If true, applies brighter color feedback.</param>
        /// <param name="isDragging">If true, applies larger marker size feedback.</param>
        private void DrawPivotMarker(
            Rect spriteRect,
            Vector2 pivot,
            Color color,
            bool isHovering,
            bool isDragging
        )
        {
            float pivotX = spriteRect.x + pivot.x * spriteRect.width;
            float pivotY = spriteRect.y + (1f - pivot.y) * spriteRect.height;

            float armLength = isDragging ? 10f : 6f;
            float armThickness = isDragging ? 3f : 2f;

            Color effectiveColor = color;
            if (isHovering && !isDragging)
            {
                effectiveColor = new Color(
                    Mathf.Min(1f, color.r * 1.3f),
                    Mathf.Min(1f, color.g * 1.3f),
                    Mathf.Min(1f, color.b * 1.3f),
                    color.a
                );
            }
            else if (isDragging)
            {
                effectiveColor = new Color(
                    Mathf.Min(1f, color.r * 1.5f),
                    Mathf.Min(1f, color.g * 1.5f),
                    Mathf.Min(1f, color.b * 1.5f),
                    color.a
                );
            }

            EditorGUI.DrawRect(
                new Rect(
                    pivotX - armLength,
                    pivotY - armThickness / 2f,
                    armLength * 2f,
                    armThickness
                ),
                effectiveColor
            );

            EditorGUI.DrawRect(
                new Rect(
                    pivotX - armThickness / 2f,
                    pivotY - armLength,
                    armThickness,
                    armLength * 2f
                ),
                effectiveColor
            );
        }

        /// <summary>
        /// Draws the sheet-level pivot marker with dashed lines across the entire preview.
        /// Uses gold/yellow color to differentiate from per-sprite markers.
        /// </summary>
        private void DrawSheetLevelPivotMarker(SpriteSheetEntry entry, Rect textureRect)
        {
            if (entry == null)
            {
                return;
            }

            PivotMode effectivePivotMode = GetEffectivePivotMode(entry);
            Vector2 effectiveCustomPivot = GetEffectiveCustomPivot(entry);
            Vector2 pivot = PivotModeToVector2(effectivePivotMode, effectiveCustomPivot);

            bool hasCustomSheetPivot =
                !entry._useGlobalSettings
                && entry._pivotModeOverride.HasValue
                && entry._pivotModeOverride.Value == PivotMode.Custom;

            if (!hasCustomSheetPivot && !_isDraggingPivot)
            {
                return;
            }

            float pivotX = textureRect.x + pivot.x * textureRect.width;
            float pivotY = textureRect.y + (1f - pivot.y) * textureRect.height;

            bool isSheetDragging =
                _isDraggingPivot
                && _draggedPivotTarget == entry
                && _draggedPivotType == PivotDragType.Sheet;
            bool isSheetHovering =
                _isHoveringPivot && _hoveredPivotTarget == entry && _hoveredSpriteIndex < 0;

            DrawDashedLine(
                new Vector2(textureRect.x, pivotY),
                new Vector2(textureRect.xMax, pivotY),
                SheetPivotColor,
                4f,
                4f
            );

            DrawDashedLine(
                new Vector2(pivotX, textureRect.y),
                new Vector2(pivotX, textureRect.yMax),
                SheetPivotColor,
                4f,
                4f
            );

            DrawPivotMarker(textureRect, pivot, SheetPivotColor, isSheetHovering, isSheetDragging);
        }

        /// <summary>
        /// Draws a dashed line between two points.
        /// </summary>
        /// <param name="start">The starting point of the line.</param>
        /// <param name="end">The ending point of the line.</param>
        /// <param name="color">The color of the dashes.</param>
        /// <param name="dashLength">The length of each dash in pixels.</param>
        /// <param name="gapLength">The length of each gap in pixels.</param>
        private static void DrawDashedLine(
            Vector2 start,
            Vector2 end,
            Color color,
            float dashLength,
            float gapLength
        )
        {
            Vector2 direction = end - start;
            float totalLength = direction.magnitude;

            if (totalLength < 0.001f)
            {
                return;
            }

            direction /= totalLength;

            float segmentLength = dashLength + gapLength;
            float currentPosition = 0f;
            bool isHorizontal = Mathf.Abs(direction.x) > Mathf.Abs(direction.y);
            float lineThickness = 1f;

            while (currentPosition < totalLength)
            {
                float dashEnd = Mathf.Min(currentPosition + dashLength, totalLength);
                Vector2 dashStart = start + direction * currentPosition;
                Vector2 dashEndPoint = start + direction * dashEnd;

                if (isHorizontal)
                {
                    EditorGUI.DrawRect(
                        new Rect(
                            dashStart.x,
                            dashStart.y - lineThickness / 2f,
                            dashEndPoint.x - dashStart.x,
                            lineThickness
                        ),
                        color
                    );
                }
                else
                {
                    EditorGUI.DrawRect(
                        new Rect(
                            dashStart.x - lineThickness / 2f,
                            dashStart.y,
                            lineThickness,
                            dashEndPoint.y - dashStart.y
                        ),
                        color
                    );
                }

                currentPosition += segmentLength;
            }
        }

        private List<SpriteEntryData> GetSortedSprites(List<SpriteEntryData> sprites)
        {
            // Use SerializedProperty value for immediate response because backing field isn't updated
            // until ApplyModifiedProperties() is called at the end of OnGUI
            SortMode currentSortMode = (SortMode)_sortModeProperty.enumValueIndex;
            bool needsRefresh =
                _cachedSortedSprites == null
                || _lastSpritesSource != sprites
                || _lastSortMode != currentSortMode;

            if (!needsRefresh)
            {
                return _cachedSortedSprites;
            }

            _cachedSortedSprites = ApplySortMode(sprites, currentSortMode, _cachedSortedSprites);
            _lastSpritesSource = sprites;
            _lastSortMode = currentSortMode;
            return _cachedSortedSprites;
        }

        /// <summary>
        /// Applies the specified sort mode to a list of sprites, returning a new sorted list.
        /// </summary>
        /// <param name="sprites">The source sprites to sort.</param>
        /// <param name="sortMode">The sort mode to apply.</param>
        /// <param name="outputList">Optional pre-allocated list to reuse. If null, a new list is created.</param>
        /// <returns>A sorted copy of the sprites list (does not modify the original).</returns>
        /// <remarks>
        /// This method is internal for testability. It creates a copy of the input list and sorts
        /// the copy, leaving the original list unchanged.
        /// </remarks>
        internal static List<SpriteEntryData> ApplySortMode(
            List<SpriteEntryData> sprites,
            SortMode sortMode,
            List<SpriteEntryData> outputList = null
        )
        {
            outputList ??= new List<SpriteEntryData>();
            outputList.Clear();

            for (int i = 0; i < sprites.Count; ++i)
            {
                outputList.Add(sprites[i]);
            }

            switch (sortMode)
            {
                case SortMode.ByName:
                    outputList.Sort(
                        (a, b) =>
                            string.Compare(
                                a._originalName,
                                b._originalName,
                                StringComparison.Ordinal
                            )
                    );
                    break;
                case SortMode.ByPositionTopLeft:
                    outputList.Sort(
                        (a, b) =>
                        {
                            int yCompare = b._rect.y.CompareTo(a._rect.y);
                            return yCompare != 0 ? yCompare : a._rect.x.CompareTo(b._rect.x);
                        }
                    );
                    break;
                case SortMode.ByPositionBottomLeft:
                    outputList.Sort(
                        (a, b) =>
                        {
                            int yCompare = a._rect.y.CompareTo(b._rect.y);
                            return yCompare != 0 ? yCompare : a._rect.x.CompareTo(b._rect.x);
                        }
                    );
                    break;
                case SortMode.Reversed:
                    outputList.Reverse();
                    break;
                case SortMode.Original:
                default:
                    break;
            }

            return outputList;
        }

        private void DrawSpriteEntry(SpriteSheetEntry sheet, SpriteEntryData sprite, int index)
        {
            int previewSize = GetPreviewSize(sprite);

            using (new EditorGUILayout.HorizontalScope())
            {
                sprite._isSelected = EditorGUILayout.Toggle(
                    sprite._isSelected,
                    GUILayout.Width(20)
                );

                if (sprite._previewTexture != null)
                {
                    GUILayout.Label(
                        sprite._previewTexture,
                        GUILayout.Width(previewSize),
                        GUILayout.Height(previewSize)
                    );
                    Rect previewRect = GUILayoutUtility.GetLastRect();

                    Vector2 effectivePivot = GetEffectivePivot(sheet, sprite);
                    bool isNonDefaultPivot =
                        effectivePivot != CenterPivot || sprite._usePivotOverride;

                    if (isNonDefaultPivot)
                    {
                        Color pivotColor = GetEffectivePivotColor(sheet, sprite);
                        DrawPivotMarker(previewRect, effectivePivot, pivotColor);
                    }
                }
                else
                {
                    using (
                        new EditorGUILayout.VerticalScope(
                            GUILayout.Width(previewSize),
                            GUILayout.Height(previewSize)
                        )
                    )
                    {
                        GUIStyle placeholderStyle = new(EditorStyles.helpBox)
                        {
                            alignment = TextAnchor.MiddleCenter,
                            fontSize = previewSize / 2,
                        };
                        GUILayout.Label(
                            "?",
                            placeholderStyle,
                            GUILayout.Width(previewSize),
                            GUILayout.Height(previewSize)
                        );
                    }
                }

                string prefix = string.IsNullOrWhiteSpace(_namingPrefix)
                    ? Path.GetFileNameWithoutExtension(sheet._assetPath)
                    : _namingPrefix;
                string previewName = $"{prefix}_{index:D3}.png";

                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(
                        $"Original: {sprite._originalName}",
                        EditorStyles.miniLabel
                    );
                    EditorGUILayout.LabelField($"Output: {previewName}", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField(
                        $"Size: {(int)sprite._rect.width}x{(int)sprite._rect.height}",
                        EditorStyles.miniLabel
                    );
                    EditorGUILayout.LabelField(
                        $"Rect: ({(int)sprite._rect.x}, {(int)sprite._rect.y})",
                        EditorStyles.miniLabel
                    );
                    string previewStatus =
                        sprite._previewTexture != null
                            ? $"Preview: {sprite._previewTexture.width}x{sprite._previewTexture.height}"
                            : "Preview: MISSING";
                    EditorGUILayout.LabelField(previewStatus, EditorStyles.miniLabel);

                    DrawSpritePivotOverrideControls(sheet, sprite);
                }
            }
        }

        /// <summary>
        /// Draws the per-element pivot override controls within a sprite entry.
        /// </summary>
        /// <param name="sheet">The parent sprite sheet entry.</param>
        /// <param name="sprite">The sprite entry to draw controls for.</param>
        private void DrawSpritePivotOverrideControls(SpriteSheetEntry sheet, SpriteEntryData sprite)
        {
            if (sprite == null)
            {
                return;
            }

            bool previousOverride = sprite._usePivotOverride;
            sprite._usePivotOverride = EditorGUILayout.ToggleLeft(
                "Override Pivot",
                sprite._usePivotOverride,
                EditorStyles.miniLabel
            );

            if (sprite._usePivotOverride && !previousOverride)
            {
                sprite._pivotModeOverride = GetEffectivePivotMode(sheet);
                sprite._customPivotOverride = GetEffectiveCustomPivot(sheet);
            }

            if (!sprite._usePivotOverride)
            {
                return;
            }

            ++EditorGUI.indentLevel;

            PivotMode newPivotMode = (PivotMode)
                EditorGUILayout.EnumPopup("Pivot Mode", sprite._pivotModeOverride);
            sprite._pivotModeOverride = newPivotMode;

            if (sprite._pivotModeOverride == PivotMode.Custom)
            {
                // X slider
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("X", GUILayout.Width(20));
                    float newX = EditorGUILayout.Slider(sprite._customPivotOverride.x, 0f, 1f);
                    if (!Mathf.Approximately(newX, sprite._customPivotOverride.x))
                    {
                        sprite._customPivotOverride = new Vector2(
                            newX,
                            sprite._customPivotOverride.y
                        );
                    }
                }

                // Y slider
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Y", GUILayout.Width(20));
                    float newY = EditorGUILayout.Slider(sprite._customPivotOverride.y, 0f, 1f);
                    if (!Mathf.Approximately(newY, sprite._customPivotOverride.y))
                    {
                        sprite._customPivotOverride = new Vector2(
                            sprite._customPivotOverride.x,
                            newY
                        );
                    }
                }

                // Combined Vector2Field for direct input with clamping
                Vector2 newPivot = EditorGUILayout.Vector2Field(
                    "Custom Pivot",
                    sprite._customPivotOverride
                );
                sprite._customPivotOverride = new Vector2(
                    Mathf.Clamp01(newPivot.x),
                    Mathf.Clamp01(newPivot.y)
                );
            }

            bool previousColorOverride = sprite._usePivotColorOverride;
            sprite._usePivotColorOverride = EditorGUILayout.ToggleLeft(
                "Override Pivot Color",
                sprite._usePivotColorOverride,
                EditorStyles.miniLabel
            );

            if (sprite._usePivotColorOverride && !previousColorOverride)
            {
                sprite._pivotColorOverride = GetEffectivePivotColor(sheet, null);
            }

            if (sprite._usePivotColorOverride)
            {
                ++EditorGUI.indentLevel;
                sprite._pivotColorOverride = EditorGUILayout.ColorField(
                    "Pivot Color",
                    sprite._pivotColorOverride
                );
                --EditorGUI.indentLevel;
            }

            --EditorGUI.indentLevel;
        }

        internal int GetPreviewSize(SpriteEntryData sprite)
        {
            switch (_previewSizeMode)
            {
                case PreviewSizeMode.Size24:
                    return 24;
                case PreviewSizeMode.Size64:
                    return 64;
                case PreviewSizeMode.RealSize:
                    if (sprite != null)
                    {
                        int maxDim = Mathf.Max((int)sprite._rect.width, (int)sprite._rect.height);
                        return Mathf.Max(16, Mathf.Min(maxDim, 128));
                    }
                    return 32;
                case PreviewSizeMode.Size32:
                default:
                    return 32;
            }
        }

        private void DrawExtractionSection()
        {
            if (_discoveredSheets == null || _discoveredSheets.Count == 0)
            {
                return;
            }

            int selectedSheetCount = 0;
            int selectedSpriteCount = 0;
            for (int i = 0; i < _discoveredSheets.Count; ++i)
            {
                SpriteSheetEntry entry = _discoveredSheets[i];
                if (!entry._isSelected)
                {
                    continue;
                }
                ++selectedSheetCount;
                if (entry._sprites != null)
                {
                    for (int j = 0; j < entry._sprites.Count; ++j)
                    {
                        if (entry._sprites[j]._isSelected)
                        {
                            ++selectedSpriteCount;
                        }
                    }
                }
            }

            EditorGUILayout.LabelField(
                $"Selected: {selectedSheetCount} sheet(s), {selectedSpriteCount} sprite(s)",
                EditorStyles.boldLabel
            );

            if (_outputDirectory == null)
            {
                EditorGUILayout.HelpBox(
                    "Output directory is required for extraction.",
                    MessageType.Info
                );
                return;
            }

            string outputPath = AssetDatabase.GetAssetPath(_outputDirectory);
            if (!AssetDatabase.IsValidFolder(outputPath))
            {
                EditorGUILayout.HelpBox(
                    "Selected output directory is not valid.",
                    MessageType.Error
                );
                return;
            }

            using (new EditorGUI.DisabledScope(selectedSpriteCount == 0))
            {
                string buttonLabel = _dryRun
                    ? $"Dry Run: Preview {selectedSpriteCount} Sprite(s)"
                    : $"Extract {selectedSpriteCount} Sprite(s)";

                if (GUILayout.Button(buttonLabel))
                {
                    ExtractSelectedSprites();
                }
            }

            if (_lastExtractedCount > 0 || _lastSkippedCount > 0 || _lastErrorCount > 0)
            {
                EditorGUILayout.HelpBox(
                    $"Last extraction: {_lastExtractedCount} extracted, {_lastSkippedCount} skipped, {_lastErrorCount} errors",
                    _lastErrorCount > 0 ? MessageType.Warning : MessageType.Info
                );
            }
        }

        private void DrawDangerZone()
        {
            using (new GUILayout.VerticalScope("box"))
            {
                Color prev = GUI.color;
                GUI.color = Color.red;
                _dangerZoneFoldout = EditorGUILayout.Foldout(
                    _dangerZoneFoldout,
                    "Danger Zone: Reference Replacement",
                    true
                );
                GUI.color = prev;

                if (!_dangerZoneFoldout)
                {
                    return;
                }

                EditorGUILayout.HelpBox(
                    "This will scan prefabs and scenes for references to original sprite sheet sprites "
                        + "and replace them with references to extracted individual sprites. This is potentially "
                        + "destructive. Ensure you have backups/version control.",
                    MessageType.Error
                );

                _ackDanger = EditorGUILayout.ToggleLeft(
                    "I understand the risks and want to proceed.",
                    _ackDanger
                );

                using (new EditorGUI.DisabledScope(!_ackDanger || _discoveredSheets == null))
                {
                    if (GUILayout.Button("Replace Sprite References With Extracted Versions"))
                    {
                        ReplaceSpriteReferences();
                    }
                }
            }
        }

        internal void DiscoverSpriteSheets(bool generatePreviews = true)
        {
            CleanupPreviewTextures();

            _discoveredSheets ??= new List<SpriteSheetEntry>();
            _discoveredSheets.Clear();

            // Compile regex from _spriteNameRegex field at the start
            _regex = null;
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
                    _regexError = e.Message;
                    Repaint();
                    return;
                }
            }

            if (_inputDirectories == null || _inputDirectories.Count == 0)
            {
                this.LogWarn($"No input directories selected.");
                Repaint();
                return;
            }

            using PooledResource<HashSet<string>> seenLease = SetBuffers<string>
                .GetHashSetPool(StringComparer.OrdinalIgnoreCase)
                .Get(out HashSet<string> seen);

            for (int dirIndex = 0; dirIndex < _inputDirectories.Count; ++dirIndex)
            {
                Object maybeDirectory = _inputDirectories[dirIndex];
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
                for (int guidIndex = 0; guidIndex < guids.Length; ++guidIndex)
                {
                    string file = AssetDatabase.GUIDToAssetPath(guids[guidIndex]);
                    if (string.IsNullOrEmpty(file))
                    {
                        continue;
                    }

                    bool hasValidExtension = false;
                    for (int extIndex = 0; extIndex < ImageFileExtensions.Length; ++extIndex)
                    {
                        if (
                            file.EndsWith(
                                ImageFileExtensions[extIndex],
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            hasValidExtension = true;
                            break;
                        }
                    }
                    if (!hasValidExtension)
                    {
                        continue;
                    }

                    string fileName = Path.GetFileNameWithoutExtension(file);
                    if (_regex != null && !_regex.IsMatch(fileName))
                    {
                        continue;
                    }

                    if (!seen.Add(file))
                    {
                        continue;
                    }

                    if (
                        AssetImporter.GetAtPath(file)
                        is not TextureImporter { textureType: TextureImporterType.Sprite } importer
                    )
                    {
                        continue;
                    }

                    SpriteSheetEntry entry = CreateSpriteSheetEntry(file, importer);
                    if (entry != null)
                    {
                        TryAutoLoadConfig(entry);
                        // Update cache key after loading config to prevent stale detection
                        // TryAutoLoadConfig may change entry settings that affect the cache key
                        entry._lastCacheKey = entry.GetBoundsCacheKey(this);
                        _discoveredSheets.Add(entry);
                    }
                }
            }

            if (generatePreviews)
            {
                if (DiagnosticsEnabled)
                {
                    this.Log(
                        $"DiscoverSpriteSheets: About to call GenerateAllPreviewTexturesInBatch with {_discoveredSheets?.Count ?? 0} entries"
                    );
                }
                GenerateAllPreviewTexturesInBatch(_discoveredSheets);
                if (DiagnosticsEnabled)
                {
                    this.Log($"DiscoverSpriteSheets: GenerateAllPreviewTexturesInBatch completed");
                }
            }

            this.Log($"Discovered {_discoveredSheets.Count} sprite sheet(s).");
            Repaint();
        }

        private SpriteSheetEntry CreateSpriteSheetEntry(string assetPath, TextureImporter importer)
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (texture == null)
            {
                return null;
            }

            SpriteSheetEntry entry = new()
            {
                _assetPath = assetPath,
                _texture = texture,
                _importer = importer,
                _importMode = importer.spriteImportMode,
                _isExpanded = false,
                _isSelected = true,
                _sprites = new List<SpriteEntryData>(),
            };

            switch (_extractionMode)
            {
                case ExtractionMode.GridBased:
                    PopulateSpritesFromGrid(entry, texture);
                    break;
                case ExtractionMode.PaddedGrid:
                    PopulateSpritesFromPaddedGrid(entry, texture);
                    break;
                case ExtractionMode.AlphaDetection:
                    PopulateSpritesFromAlphaDetection(entry, texture);
                    break;
                case ExtractionMode.FromMetadata:
                default:
                    PopulateSpritesFromMetadata(entry, assetPath, importer);
                    break;
            }

            // Initialize cache key to prevent entries appearing stale immediately after creation
            entry._lastCacheKey = entry.GetBoundsCacheKey(this);
            entry._lastAccessTime = DateTime.UtcNow.Ticks;
            if (DiagnosticsEnabled)
            {
                this.Log(
                    $"CreateSpriteSheetEntry: created entry for '{assetPath}', spriteCount={entry._sprites.Count}, initialCacheKey={entry._lastCacheKey}"
                );
            }

            return entry;
        }

        /// <summary>
        /// Generates preview thumbnails for all sprites in the specified sprite sheet entry.
        /// </summary>
        /// <param name="entry">The sprite sheet entry containing sprites to generate previews for.</param>
        /// <remarks>
        /// Temporarily enables Read/Write on the texture if needed, generates scaled preview
        /// textures for each sprite, then restores the original readable state. Compressed or
        /// unsupported texture formats will skip preview generation with a warning.
        /// </remarks>
        private void GeneratePreviewTexturesForEntry(SpriteSheetEntry entry)
        {
            if (
                entry == null
                || entry._sprites == null
                || entry._sprites.Count == 0
                || entry._texture == null
            )
            {
                return;
            }

            bool wasReadable = entry._importer.isReadable;
            bool needsReimport = false;

            try
            {
                if (!wasReadable)
                {
                    entry._importer.isReadable = true;
                    entry._importer.SaveAndReimport();
                    needsReimport = true;
                    entry._texture = AssetDatabase.LoadAssetAtPath<Texture2D>(entry._assetPath);
                    if (entry._texture == null)
                    {
                        return;
                    }
                }

                if (!IsTextureFormatSupportedForGetPixels(entry._texture.format))
                {
                    this.LogWarn(
                        $"Texture format '{entry._texture.format}' does not support GetPixels32 for {entry._assetPath}. Preview generation skipped."
                    );
                    return;
                }

                Color32[] sourcePixels = entry._texture.GetPixels32();
                int sourceWidth = entry._texture.width;
                int sourceHeight = entry._texture.height;

                for (int i = 0; i < entry._sprites.Count; ++i)
                {
                    SpriteEntryData sprite = entry._sprites[i];
                    if (sprite == null)
                    {
                        continue;
                    }

                    // Keep old texture until new one is ready to avoid grey question marks
                    Texture2D oldTexture = sprite._previewTexture;
                    Texture2D preview = GenerateSinglePreviewTexture(
                        sourcePixels,
                        sourceWidth,
                        sourceHeight,
                        sprite
                    );
                    sprite._previewTexture = preview;

                    // Destroy old texture after new one is assigned
                    if (oldTexture != null)
                    {
                        DestroyImmediate(oldTexture);
                    }
                }
            }
            catch (Exception e)
            {
                this.LogWarn($"Failed to generate preview textures for {entry._assetPath}", e);
            }
            finally
            {
                if (needsReimport)
                {
                    entry._importer.isReadable = false;
                    entry._importer.SaveAndReimport();
                    entry._texture = AssetDatabase.LoadAssetAtPath<Texture2D>(entry._assetPath);
                }
            }
        }

        /// <summary>
        /// Generates preview textures for all sprite sheet entries in a batch operation.
        /// </summary>
        /// <param name="entries">The list of sprite sheet entries to generate previews for.</param>
        /// <remarks>
        /// This method batches texture readability changes to minimize reimport operations.
        /// It uses <see cref="AssetDatabaseBatchHelper.BeginBatch"/> to batch all import changes together,
        /// improving performance when processing multiple textures.
        /// </remarks>
        internal void GenerateAllPreviewTexturesInBatch(List<SpriteSheetEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                if (DiagnosticsEnabled)
                {
                    this.Log(
                        $"GenerateAllPreviewTexturesInBatch: entries null or empty, returning early"
                    );
                }
                return;
            }

            if (DiagnosticsEnabled)
            {
                this.Log($"GenerateAllPreviewTexturesInBatch: START with {entries.Count} entries");
            }
            WallstopGenericPool<Dictionary<string, bool>> originalReadablePool = DictionaryBuffer<
                string,
                bool
            >.GetDictionaryPool(StringComparer.OrdinalIgnoreCase);
            using PooledResource<Dictionary<string, bool>> originalReadableLease =
                originalReadablePool.Get(out Dictionary<string, bool> originalReadable);

            using PooledResource<List<SpriteSheetEntry>> needsReadableLease =
                Buffers<SpriteSheetEntry>.List.Get(out List<SpriteSheetEntry> needsReadable);

            for (int i = 0; i < entries.Count; ++i)
            {
                SpriteSheetEntry entry = entries[i];
                if (
                    entry == null
                    || entry._sprites == null
                    || entry._sprites.Count == 0
                    || entry._texture == null
                    || entry._importer == null
                )
                {
                    continue;
                }

                originalReadable[entry._assetPath] = entry._importer.isReadable;

                if (!entry._importer.isReadable)
                {
                    needsReadable.Add(entry);
                }
            }

            if (needsReadable.Count > 0)
            {
                using (AssetDatabaseBatchHelper.BeginBatch())
                {
                    for (int i = 0; i < needsReadable.Count; ++i)
                    {
                        SpriteSheetEntry entry = needsReadable[i];
                        entry._importer.isReadable = true;
                        entry._importer.SaveAndReimport();
                    }
                }

                AssetDatabase.SaveAssets();

                for (int i = 0; i < needsReadable.Count; ++i)
                {
                    SpriteSheetEntry entry = needsReadable[i];
                    entry._texture = AssetDatabase.LoadAssetAtPath<Texture2D>(entry._assetPath);
                    if (entry._texture == null)
                    {
                        this.LogWarn(
                            $"Failed to reload texture after making readable: {entry._assetPath}"
                        );
                        continue;
                    }
                }
            }

            for (int i = 0; i < entries.Count; ++i)
            {
                SpriteSheetEntry entry = entries[i];
                if (
                    entry == null
                    || entry._sprites == null
                    || entry._sprites.Count == 0
                    || entry._texture == null
                )
                {
                    continue;
                }

                if (!IsTextureFormatSupportedForGetPixels(entry._texture.format))
                {
                    this.LogWarn(
                        $"Texture format '{entry._texture.format}' does not support GetPixels32 for {entry._assetPath}. Preview generation skipped."
                    );
                    continue;
                }

                try
                {
                    Color32[] sourcePixels = entry._texture.GetPixels32();
                    int sourceWidth = entry._texture.width;
                    int sourceHeight = entry._texture.height;

                    for (int j = 0; j < entry._sprites.Count; ++j)
                    {
                        SpriteEntryData sprite = entry._sprites[j];
                        if (sprite == null)
                        {
                            continue;
                        }

                        // Keep old texture until new one is ready to avoid grey question marks
                        Texture2D oldTexture = sprite._previewTexture;
                        Texture2D preview = GenerateSinglePreviewTexture(
                            sourcePixels,
                            sourceWidth,
                            sourceHeight,
                            sprite
                        );
                        sprite._previewTexture = preview;
                        if (DiagnosticsEnabled)
                        {
                            this.Log(
                                $"GenerateAllPreviewTexturesInBatch: Generated preview for sprite '{sprite._originalName}' in '{entry._assetPath}', preview={preview != null}"
                            );
                        }

                        // Destroy old texture after new one is assigned
                        if (oldTexture != null)
                        {
                            DestroyImmediate(oldTexture);
                        }
                    }
                }
                catch (Exception e)
                {
                    this.LogWarn($"Failed to generate preview textures for {entry._assetPath}", e);
                }
            }

            using PooledResource<List<SpriteSheetEntry>> needsRestoreLease =
                Buffers<SpriteSheetEntry>.List.Get(out List<SpriteSheetEntry> needsRestore);

            for (int i = 0; i < entries.Count; ++i)
            {
                SpriteSheetEntry entry = entries[i];
                if (entry == null || entry._importer == null)
                {
                    continue;
                }

                if (
                    originalReadable.TryGetValue(entry._assetPath, out bool wasReadable)
                    && !wasReadable
                )
                {
                    needsRestore.Add(entry);
                }
            }

            if (needsRestore.Count > 0)
            {
                using (AssetDatabaseBatchHelper.BeginBatch())
                {
                    for (int i = 0; i < needsRestore.Count; ++i)
                    {
                        SpriteSheetEntry entry = needsRestore[i];
                        entry._importer.isReadable = false;
                        entry._importer.SaveAndReimport();
                    }
                }

                AssetDatabase.SaveAssets();

                for (int i = 0; i < needsRestore.Count; ++i)
                {
                    SpriteSheetEntry entry = needsRestore[i];
                    entry._texture = AssetDatabase.LoadAssetAtPath<Texture2D>(entry._assetPath);
                }

                // Schedule repaint after asset database operations complete
                SpriteSheetExtractor windowRef = this;
                EditorApplication.delayCall += () =>
                {
                    // Use implicit bool for Unity object null check
                    if (windowRef)
                    {
                        windowRef.Repaint();
                    }
                };
            }
            if (DiagnosticsEnabled)
            {
                this.Log($"GenerateAllPreviewTexturesInBatch: END");
            }
        }

        /// <summary>
        /// Creates a single preview texture for a sprite by extracting and scaling pixels from the source texture.
        /// </summary>
        /// <param name="sourcePixels">The pixel data from the source texture.</param>
        /// <param name="sourceWidth">The width of the source texture in pixels.</param>
        /// <param name="sourceHeight">The height of the source texture in pixels.</param>
        /// <param name="sprite">The sprite entry containing rect and size information.</param>
        /// <returns>A scaled preview Texture2D, or null if the sprite is invalid or has zero dimensions.</returns>
        /// <remarks>
        /// Uses nearest-neighbor sampling for pixel-perfect scaling. The preview size is determined
        /// by the current <see cref="_previewSizeMode"/> setting. Uses pooled arrays to minimize
        /// GC allocations during preview generation.
        /// </remarks>
        private Texture2D GenerateSinglePreviewTexture(
            Color32[] sourcePixels,
            int sourceWidth,
            int sourceHeight,
            SpriteEntryData sprite
        )
        {
            if (sprite == null)
            {
                this.LogWarn($"GenerateSinglePreviewTexture: sprite is null, skipping preview.");
                return null;
            }

            int spriteX = Mathf.FloorToInt(sprite._rect.x);
            int spriteY = Mathf.FloorToInt(sprite._rect.y);
            int spriteWidth = Mathf.FloorToInt(sprite._rect.width);
            int spriteHeight = Mathf.FloorToInt(sprite._rect.height);

            if (spriteWidth <= 0 || spriteHeight <= 0)
            {
                this.LogWarn(
                    $"GenerateSinglePreviewTexture: Invalid dimensions for '{sprite._originalName}' - width={spriteWidth}, height={spriteHeight}, rect=({sprite._rect.x}, {sprite._rect.y}, {sprite._rect.width}, {sprite._rect.height})"
                );
                return null;
            }

            spriteX = Mathf.Clamp(spriteX, 0, sourceWidth - 1);
            spriteY = Mathf.Clamp(spriteY, 0, sourceHeight - 1);
            spriteWidth = Mathf.Clamp(spriteWidth, 1, sourceWidth - spriteX);
            spriteHeight = Mathf.Clamp(spriteHeight, 1, sourceHeight - spriteY);

            int previewSize = GetPreviewSize(sprite);
            int targetWidth;
            int targetHeight;

            if (_previewSizeMode == PreviewSizeMode.RealSize)
            {
                int maxDim = Mathf.Max(spriteWidth, spriteHeight);
                float scale = Mathf.Min(1f, (float)previewSize / maxDim);
                targetWidth = Mathf.Max(1, Mathf.RoundToInt(spriteWidth * scale));
                targetHeight = Mathf.Max(1, Mathf.RoundToInt(spriteHeight * scale));
            }
            else
            {
                if (spriteWidth >= spriteHeight)
                {
                    targetWidth = previewSize;
                    targetHeight = Mathf.Max(
                        1,
                        Mathf.RoundToInt(previewSize * (float)spriteHeight / spriteWidth)
                    );
                }
                else
                {
                    targetHeight = previewSize;
                    targetWidth = Mathf.Max(
                        1,
                        Mathf.RoundToInt(previewSize * (float)spriteWidth / spriteHeight)
                    );
                }
            }

            int pixelCount = targetWidth * targetHeight;
            // Note: Cannot use pooled arrays here because SetPixels32 requires the array length
            // to exactly match the texture dimensions, but ArrayPool returns arrays that may be
            // larger than requested.
            Color32[] destPixels = new Color32[pixelCount];

            float xRatio = (float)spriteWidth / targetWidth;
            float yRatio = (float)spriteHeight / targetHeight;

            for (int destY = 0; destY < targetHeight; ++destY)
            {
                int srcY = spriteY + Mathf.FloorToInt(destY * yRatio);
                srcY = Mathf.Clamp(srcY, 0, sourceHeight - 1);
                int destRowStart = destY * targetWidth;
                int srcRowStart = srcY * sourceWidth;

                for (int destX = 0; destX < targetWidth; ++destX)
                {
                    int srcX = spriteX + Mathf.FloorToInt(destX * xRatio);
                    srcX = Mathf.Clamp(srcX, 0, sourceWidth - 1);
                    destPixels[destRowStart + destX] = sourcePixels[srcRowStart + srcX];
                }
            }

            Texture2D preview = null;
            try
            {
                preview = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
                preview.SetPixels32(destPixels);
                preview.Apply();
                return preview;
            }
            catch
            {
                if (preview != null)
                {
                    DestroyImmediate(preview);
                }
                throw;
            }
        }

        /// <summary>
        /// Regenerates preview textures for all discovered sprite sheets, including repopulating sprites.
        /// </summary>
        /// <remarks>
        /// Called when extraction mode changes, which requires repopulating the sprite list
        /// in addition to regenerating preview thumbnails. Uses batch processing to minimize
        /// reimport operations. For preview size changes only, use <see cref="RegeneratePreviewTexturesOnly"/>.
        /// </remarks>
        internal void RegenerateAllPreviewTextures()
        {
            if (DiagnosticsEnabled)
            {
                this.Log($"RegenerateAllPreviewTextures: START");
            }
            _previewRegenerationScheduled = false;

            if (!this || _discoveredSheets == null || _discoveredSheets.Count == 0)
            {
                if (DiagnosticsEnabled)
                {
                    this.Log(
                        $"RegenerateAllPreviewTextures: no sheets discovered, returning early"
                    );
                }
                return;
            }

            if (DiagnosticsEnabled)
            {
                this.Log(
                    $"RegenerateAllPreviewTextures: setting _regenerationInProgress=true, sheetCount={_discoveredSheets.Count}"
                );
            }
            _regenerationInProgress = true;

            // Collect old textures to destroy after new ones are generated
            using PooledResource<List<Texture2D>> oldTexturesLease = Buffers<Texture2D>.List.Get(
                out List<Texture2D> oldTextures
            );

            for (int i = 0; i < _discoveredSheets.Count; ++i)
            {
                SpriteSheetEntry entry = _discoveredSheets[i];
                if (entry == null)
                {
                    continue;
                }

                if (entry._sprites != null)
                {
                    for (int j = 0; j < entry._sprites.Count; ++j)
                    {
                        SpriteEntryData sprite = entry._sprites[j];
                        if (sprite != null && sprite._previewTexture != null)
                        {
                            oldTextures.Add(sprite._previewTexture);
                            sprite._previewTexture = null;
                        }
                    }
                }

                RepopulateSpritesForEntry(entry);
                // Update cache state after repopulating sprites
                entry._needsRegeneration = false;
                entry._lastCacheKey = entry.GetBoundsCacheKey(this);
                entry._lastAccessTime = DateTime.UtcNow.Ticks;
            }

            GenerateAllPreviewTexturesInBatch(_discoveredSheets);

            // Destroy old textures after new ones are generated
            for (int i = 0; i < oldTextures.Count; ++i)
            {
                Texture2D oldTexture = oldTextures[i];
                if (oldTexture != null)
                {
                    DestroyImmediate(oldTexture);
                }
            }

            if (DiagnosticsEnabled)
            {
                this.Log(
                    $"RegenerateAllPreviewTextures: END, setting _regenerationInProgress=false"
                );
            }
            _regenerationInProgress = false;
            Repaint();
        }

        /// <summary>
        /// Regenerates only the preview textures without repopulating sprites.
        /// </summary>
        /// <remarks>
        /// Called when only the preview size mode changes. Since sprite rects don't change,
        /// we can skip repopulating and just regenerate the textures at the new size.
        /// This avoids the window where sprites have null textures during repopulation.
        /// </remarks>
        internal void RegeneratePreviewTexturesOnly()
        {
            if (DiagnosticsEnabled)
            {
                this.Log($"RegeneratePreviewTexturesOnly: START");
            }
            _previewRegenerationScheduled = false;

            if (!this || _discoveredSheets == null || _discoveredSheets.Count == 0)
            {
                if (DiagnosticsEnabled)
                {
                    this.Log(
                        $"RegeneratePreviewTexturesOnly: no sheets discovered, returning early"
                    );
                }
                return;
            }

            if (DiagnosticsEnabled)
            {
                this.Log(
                    $"RegeneratePreviewTexturesOnly: setting _regenerationInProgress=true, sheetCount={_discoveredSheets.Count}"
                );
            }
            _regenerationInProgress = true;

            // GenerateAllPreviewTexturesInBatch handles old texture cleanup atomically
            // by keeping old texture until new one is assigned, then destroying old
            GenerateAllPreviewTexturesInBatch(_discoveredSheets);

            if (DiagnosticsEnabled)
            {
                this.Log(
                    $"RegeneratePreviewTexturesOnly: END, setting _regenerationInProgress=false"
                );
            }
            _regenerationInProgress = false;
            Repaint();
        }

        private void PopulateSpritesFromMetadata(
            SpriteSheetEntry entry,
            string assetPath,
            TextureImporter importer
        )
        {
            if (importer.spriteImportMode == SpriteImportMode.Multiple)
            {
                Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                int spriteIndex = 0;
                for (int i = 0; i < allAssets.Length; ++i)
                {
                    if (allAssets[i] is not Sprite sprite)
                    {
                        continue;
                    }

                    SpriteEntryData spriteEntry = new()
                    {
                        _originalName = sprite.name,
                        _outputName = sprite.name,
                        _rect = sprite.rect,
                        _pivot = sprite.pivot / sprite.rect.size,
                        _border = sprite.border,
                        _sortIndex = spriteIndex++,
                        _isSelected = true,
                        _previewTexture = null,
                        _usePivotOverride = false,
                        _pivotModeOverride = PivotMode.Center,
                        _customPivotOverride = new Vector2(0.5f, 0.5f),
                        _usePivotColorOverride = false,
                        _pivotColorOverride = Color.cyan,
                    };
                    entry._sprites.Add(spriteEntry);
                }
            }
            else
            {
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                if (sprite != null)
                {
                    SpriteEntryData spriteEntry = new()
                    {
                        _originalName = sprite.name,
                        _outputName = sprite.name,
                        _rect = sprite.rect,
                        _pivot = importer.spritePivot,
                        _border = sprite.border,
                        _sortIndex = 0,
                        _isSelected = true,
                        _previewTexture = null,
                        _usePivotOverride = false,
                        _pivotModeOverride = PivotMode.Center,
                        _customPivotOverride = new Vector2(0.5f, 0.5f),
                        _usePivotColorOverride = false,
                        _pivotColorOverride = Color.cyan,
                    };
                    entry._sprites.Add(spriteEntry);
                }
            }
        }

        internal void PopulateSpritesFromGrid(SpriteSheetEntry entry, Texture2D texture)
        {
            int columns;
            int rows;
            int cellWidth;
            int cellHeight;

            Color32[] pixels = null;
            if (texture.isReadable)
            {
                pixels = texture.GetPixels32();
            }

            CalculateGridDimensions(
                texture.width,
                texture.height,
                entry,
                pixels,
                out columns,
                out rows,
                out cellWidth,
                out cellHeight
            );

            int spriteIndex = 0;
            for (int row = 0; row < rows; ++row)
            {
                for (int col = 0; col < columns; ++col)
                {
                    int x = col * cellWidth;
                    int y = (rows - 1 - row) * cellHeight;

                    Rect rect = new Rect(x, y, cellWidth, cellHeight);

                    SpriteEntryData spriteEntry = new()
                    {
                        _originalName = $"sprite_{row}_{col}",
                        _outputName = $"sprite_{row}_{col}",
                        _rect = rect,
                        _pivot = new Vector2(0.5f, 0.5f),
                        _border = Vector4.zero,
                        _sortIndex = spriteIndex++,
                        _isSelected = true,
                        _previewTexture = null,
                        _usePivotOverride = false,
                        _pivotModeOverride = PivotMode.Center,
                        _customPivotOverride = new Vector2(0.5f, 0.5f),
                        _usePivotColorOverride = false,
                        _pivotColorOverride = Color.cyan,
                    };
                    entry._sprites.Add(spriteEntry);
                }
            }
        }

        internal void PopulateSpritesFromPaddedGrid(SpriteSheetEntry entry, Texture2D texture)
        {
            int columns;
            int rows;
            int cellWidth;
            int cellHeight;

            Color32[] pixels = null;
            if (texture.isReadable)
            {
                pixels = texture.GetPixels32();
            }

            CalculateGridDimensions(
                texture.width,
                texture.height,
                entry,
                pixels,
                out columns,
                out rows,
                out cellWidth,
                out cellHeight
            );

            int effectivePaddingLeft = GetEffectivePaddingLeft(entry);
            int effectivePaddingRight = GetEffectivePaddingRight(entry);
            int effectivePaddingTop = GetEffectivePaddingTop(entry);
            int effectivePaddingBottom = GetEffectivePaddingBottom(entry);

            int paddedWidth = cellWidth - effectivePaddingLeft - effectivePaddingRight;
            int paddedHeight = cellHeight - effectivePaddingTop - effectivePaddingBottom;

            if (paddedWidth <= 0 || paddedHeight <= 0)
            {
                this.LogWarn(
                    $"Padding values result in invalid sprite size for texture {entry._assetPath}. Cell: {cellWidth}x{cellHeight}, Padding removes: {effectivePaddingLeft + effectivePaddingRight}x{effectivePaddingTop + effectivePaddingBottom}"
                );
                return;
            }

            int spriteIndex = 0;
            for (int row = 0; row < rows; ++row)
            {
                for (int col = 0; col < columns; ++col)
                {
                    int baseX = col * cellWidth;
                    int baseY = (rows - 1 - row) * cellHeight;

                    int x = baseX + effectivePaddingLeft;
                    int y = baseY + effectivePaddingBottom;

                    Rect rect = new Rect(x, y, paddedWidth, paddedHeight);

                    SpriteEntryData spriteEntry = new()
                    {
                        _originalName = $"sprite_{row}_{col}",
                        _outputName = $"sprite_{row}_{col}",
                        _rect = rect,
                        _pivot = new Vector2(0.5f, 0.5f),
                        _border = Vector4.zero,
                        _sortIndex = spriteIndex++,
                        _isSelected = true,
                        _previewTexture = null,
                        _usePivotOverride = false,
                        _pivotModeOverride = PivotMode.Center,
                        _customPivotOverride = new Vector2(0.5f, 0.5f),
                        _usePivotColorOverride = false,
                        _pivotColorOverride = Color.cyan,
                    };
                    entry._sprites.Add(spriteEntry);
                }
            }
        }

        internal void PopulateSpritesFromAlphaDetection(SpriteSheetEntry entry, Texture2D texture)
        {
            if (!texture.isReadable)
            {
                this.LogWarn(
                    $"Texture {entry._assetPath} is not readable. Enable 'Read/Write' in import settings for alpha detection."
                );
                return;
            }

            int textureWidth = texture.width;
            int textureHeight = texture.height;
            Color32[] pixels = texture.GetPixels32();

            using PooledResource<List<Rect>> rectsLease = Buffers<Rect>.List.Get(
                out List<Rect> detectedRects
            );

            float effectiveAlphaThreshold = GetEffectiveAlphaThreshold(entry);
            DetectSpriteBoundsByAlpha(
                pixels,
                textureWidth,
                textureHeight,
                effectiveAlphaThreshold,
                detectedRects
            );

            int spriteIndex = 0;
            for (int i = 0; i < detectedRects.Count; ++i)
            {
                Rect rect = detectedRects[i];

                SpriteEntryData spriteEntry = new()
                {
                    _originalName = $"sprite_{spriteIndex}",
                    _outputName = $"sprite_{spriteIndex}",
                    _rect = rect,
                    _pivot = new Vector2(0.5f, 0.5f),
                    _border = Vector4.zero,
                    _sortIndex = spriteIndex,
                    _isSelected = true,
                    _previewTexture = null,
                    _usePivotOverride = false,
                    _pivotModeOverride = PivotMode.Center,
                    _customPivotOverride = new Vector2(0.5f, 0.5f),
                    _usePivotColorOverride = false,
                    _pivotColorOverride = Color.cyan,
                };
                entry._sprites.Add(spriteEntry);
                ++spriteIndex;
            }
        }

        private void PopulateSpritesFromMetadataIntoList(
            SpriteSheetEntry entry,
            string assetPath,
            TextureImporter importer,
            List<SpriteEntryData> targetList
        )
        {
            if (importer.spriteImportMode == SpriteImportMode.Multiple)
            {
                Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                int spriteIndex = 0;
                for (int i = 0; i < allAssets.Length; ++i)
                {
                    if (allAssets[i] is not Sprite sprite)
                    {
                        continue;
                    }

                    SpriteEntryData spriteEntry = new()
                    {
                        _originalName = sprite.name,
                        _outputName = sprite.name,
                        _rect = sprite.rect,
                        _pivot = sprite.pivot / sprite.rect.size,
                        _border = sprite.border,
                        _sortIndex = spriteIndex++,
                        _isSelected = true,
                        _previewTexture = null,
                        _usePivotOverride = false,
                        _pivotModeOverride = PivotMode.Center,
                        _customPivotOverride = new Vector2(0.5f, 0.5f),
                        _usePivotColorOverride = false,
                        _pivotColorOverride = Color.cyan,
                    };
                    targetList.Add(spriteEntry);
                }
            }
            else
            {
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                if (sprite != null)
                {
                    SpriteEntryData spriteEntry = new()
                    {
                        _originalName = sprite.name,
                        _outputName = sprite.name,
                        _rect = sprite.rect,
                        _pivot = importer.spritePivot,
                        _border = sprite.border,
                        _sortIndex = 0,
                        _isSelected = true,
                        _previewTexture = null,
                        _usePivotOverride = false,
                        _pivotModeOverride = PivotMode.Center,
                        _customPivotOverride = new Vector2(0.5f, 0.5f),
                        _usePivotColorOverride = false,
                        _pivotColorOverride = Color.cyan,
                    };
                    targetList.Add(spriteEntry);
                }
            }
        }

        private void PopulateSpritesFromGridIntoList(
            SpriteSheetEntry entry,
            Texture2D texture,
            List<SpriteEntryData> targetList
        )
        {
            int columns;
            int rows;
            int cellWidth;
            int cellHeight;

            Color32[] pixels = null;

            // For Auto grid size mode, we need pixel data for algorithm detection
            GridSizeMode effectiveGridSizeMode = GetEffectiveGridSizeMode(entry);
            bool needsPixels = effectiveGridSizeMode == GridSizeMode.Auto;

            if (needsPixels)
            {
                // Use MakeReadable extension to ensure texture is readable
                texture.MakeReadable();

                // Reload texture after potential reimport
                if (!texture.isReadable)
                {
                    texture = AssetDatabase.LoadAssetAtPath<Texture2D>(entry._assetPath);
                    entry._texture = texture;
                }

                if (texture == null || !texture.isReadable)
                {
                    this.LogError(
                        $"Failed to make texture readable for algorithm detection: {entry._assetPath}. Cannot detect grid automatically."
                    );
                    entry._lastAlgorithmDisplayText = "Error: Texture not readable";
                    return;
                }

                pixels = texture.GetPixels32();
            }

            CalculateGridDimensions(
                texture.width,
                texture.height,
                entry,
                pixels,
                out columns,
                out rows,
                out cellWidth,
                out cellHeight
            );

            if (DiagnosticsEnabled && entry != null)
            {
                this.Log(
                    $"PopulateSpritesFromGridIntoList for '{Path.GetFileName(entry._assetPath)}': columns={columns}, rows={rows}, cellWidth={cellWidth}, cellHeight={cellHeight}, totalSprites={columns * rows}"
                );
            }

            int spriteIndex = 0;
            for (int row = 0; row < rows; ++row)
            {
                for (int col = 0; col < columns; ++col)
                {
                    int x = col * cellWidth;
                    int y = (rows - 1 - row) * cellHeight;

                    Rect rect = new Rect(x, y, cellWidth, cellHeight);

                    SpriteEntryData spriteEntry = new()
                    {
                        _originalName = $"sprite_{row}_{col}",
                        _outputName = $"sprite_{row}_{col}",
                        _rect = rect,
                        _pivot = new Vector2(0.5f, 0.5f),
                        _border = Vector4.zero,
                        _sortIndex = spriteIndex++,
                        _isSelected = true,
                        _previewTexture = null,
                        _usePivotOverride = false,
                        _pivotModeOverride = PivotMode.Center,
                        _customPivotOverride = new Vector2(0.5f, 0.5f),
                        _usePivotColorOverride = false,
                        _pivotColorOverride = Color.cyan,
                    };
                    targetList.Add(spriteEntry);
                }
            }
        }

        private void PopulateSpritesFromPaddedGridIntoList(
            SpriteSheetEntry entry,
            Texture2D texture,
            List<SpriteEntryData> targetList
        )
        {
            int columns;
            int rows;
            int cellWidth;
            int cellHeight;

            Color32[] pixels = null;

            // For Auto grid size mode, we need pixel data for algorithm detection
            GridSizeMode effectiveGridSizeMode = GetEffectiveGridSizeMode(entry);
            bool needsPixels = effectiveGridSizeMode == GridSizeMode.Auto;

            if (needsPixels)
            {
                // Use MakeReadable extension to ensure texture is readable
                texture.MakeReadable();

                // Reload texture after potential reimport
                if (!texture.isReadable)
                {
                    texture = AssetDatabase.LoadAssetAtPath<Texture2D>(entry._assetPath);
                    entry._texture = texture;
                }

                if (texture == null || !texture.isReadable)
                {
                    this.LogError(
                        $"Failed to make texture readable for algorithm detection: {entry._assetPath}. Cannot detect grid automatically."
                    );
                    entry._lastAlgorithmDisplayText = "Error: Texture not readable";
                    return;
                }

                pixels = texture.GetPixels32();
            }

            CalculateGridDimensions(
                texture.width,
                texture.height,
                entry,
                pixels,
                out columns,
                out rows,
                out cellWidth,
                out cellHeight
            );

            int effectivePaddingLeft = GetEffectivePaddingLeft(entry);
            int effectivePaddingRight = GetEffectivePaddingRight(entry);
            int effectivePaddingTop = GetEffectivePaddingTop(entry);
            int effectivePaddingBottom = GetEffectivePaddingBottom(entry);

            int paddedWidth = cellWidth - effectivePaddingLeft - effectivePaddingRight;
            int paddedHeight = cellHeight - effectivePaddingTop - effectivePaddingBottom;

            if (paddedWidth <= 0 || paddedHeight <= 0)
            {
                this.LogWarn(
                    $"Padding values result in invalid sprite size for texture {entry._assetPath}. Cell: {cellWidth}x{cellHeight}, Padding removes: {effectivePaddingLeft + effectivePaddingRight}x{effectivePaddingTop + effectivePaddingBottom}"
                );
                return;
            }

            int spriteIndex = 0;
            for (int row = 0; row < rows; ++row)
            {
                for (int col = 0; col < columns; ++col)
                {
                    int baseX = col * cellWidth;
                    int baseY = (rows - 1 - row) * cellHeight;

                    int x = baseX + effectivePaddingLeft;
                    int y = baseY + effectivePaddingBottom;

                    Rect rect = new Rect(x, y, paddedWidth, paddedHeight);

                    SpriteEntryData spriteEntry = new()
                    {
                        _originalName = $"sprite_{row}_{col}",
                        _outputName = $"sprite_{row}_{col}",
                        _rect = rect,
                        _pivot = new Vector2(0.5f, 0.5f),
                        _border = Vector4.zero,
                        _sortIndex = spriteIndex++,
                        _isSelected = true,
                        _previewTexture = null,
                        _usePivotOverride = false,
                        _pivotModeOverride = PivotMode.Center,
                        _customPivotOverride = new Vector2(0.5f, 0.5f),
                        _usePivotColorOverride = false,
                        _pivotColorOverride = Color.cyan,
                    };
                    targetList.Add(spriteEntry);
                }
            }
        }

        private void PopulateSpritesFromAlphaDetectionIntoList(
            SpriteSheetEntry entry,
            Texture2D texture,
            List<SpriteEntryData> targetList
        )
        {
            // Use MakeReadable extension to ensure texture is readable
            texture.MakeReadable();

            // Reload texture after potential reimport
            if (!texture.isReadable)
            {
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(entry._assetPath);
                entry._texture = texture;
            }

            if (texture == null || !texture.isReadable)
            {
                this.LogError(
                    $"Failed to make texture readable for alpha detection: {entry._assetPath}."
                );
                return;
            }

            int textureWidth = texture.width;
            int textureHeight = texture.height;
            Color32[] pixels = texture.GetPixels32();

            using PooledResource<List<Rect>> rectsLease = Buffers<Rect>.List.Get(
                out List<Rect> detectedRects
            );

            float effectiveAlphaThreshold = GetEffectiveAlphaThreshold(entry);
            DetectSpriteBoundsByAlpha(
                pixels,
                textureWidth,
                textureHeight,
                effectiveAlphaThreshold,
                detectedRects
            );

            int spriteIndex = 0;
            for (int i = 0; i < detectedRects.Count; ++i)
            {
                Rect rect = detectedRects[i];

                SpriteEntryData spriteEntry = new()
                {
                    _originalName = $"sprite_{spriteIndex}",
                    _outputName = $"sprite_{spriteIndex}",
                    _rect = rect,
                    _pivot = new Vector2(0.5f, 0.5f),
                    _border = Vector4.zero,
                    _sortIndex = spriteIndex,
                    _isSelected = true,
                    _previewTexture = null,
                    _usePivotOverride = false,
                    _pivotModeOverride = PivotMode.Center,
                    _customPivotOverride = new Vector2(0.5f, 0.5f),
                    _usePivotColorOverride = false,
                    _pivotColorOverride = Color.cyan,
                };
                targetList.Add(spriteEntry);
                ++spriteIndex;
            }
        }

        internal static void DetectSpriteBoundsByAlpha(
            Color32[] pixels,
            int textureWidth,
            int textureHeight,
            float alphaThreshold,
            List<Rect> result
        )
        {
            result.Clear();

            byte alphaThresholdByte = (byte)(alphaThreshold * 255f);
            using PooledArray<bool> visitedLease = SystemArrayPool<bool>.Get(
                pixels.Length,
                out bool[] visited
            );
            Array.Clear(visited, 0, visited.Length);

            for (int y = 0; y < textureHeight; ++y)
            {
                for (int x = 0; x < textureWidth; ++x)
                {
                    int index = y * textureWidth + x;
                    if (visited[index])
                    {
                        continue;
                    }

                    if (pixels[index].a <= alphaThresholdByte)
                    {
                        visited[index] = true;
                        continue;
                    }

                    int minX = x;
                    int maxX = x;
                    int minY = y;
                    int maxY = y;

                    using PooledResource<List<int>> stackLease = Buffers<int>.List.Get(
                        out List<int> stack
                    );
                    stack.Add(index);
                    visited[index] = true;

                    while (stack.Count > 0)
                    {
                        int lastIndex = stack.Count - 1;
                        int current = stack[lastIndex];
                        stack.RemoveAt(lastIndex);

                        int currentX = current % textureWidth;
                        int currentY = current / textureWidth;

                        if (currentX < minX)
                        {
                            minX = currentX;
                        }
                        if (currentX > maxX)
                        {
                            maxX = currentX;
                        }
                        if (currentY < minY)
                        {
                            minY = currentY;
                        }
                        if (currentY > maxY)
                        {
                            maxY = currentY;
                        }

                        if (currentX > 0)
                        {
                            int leftIndex = current - 1;
                            if (!visited[leftIndex] && pixels[leftIndex].a > alphaThresholdByte)
                            {
                                visited[leftIndex] = true;
                                stack.Add(leftIndex);
                            }
                        }
                        if (currentX < textureWidth - 1)
                        {
                            int rightIndex = current + 1;
                            if (!visited[rightIndex] && pixels[rightIndex].a > alphaThresholdByte)
                            {
                                visited[rightIndex] = true;
                                stack.Add(rightIndex);
                            }
                        }
                        if (currentY > 0)
                        {
                            int bottomIndex = current - textureWidth;
                            if (!visited[bottomIndex] && pixels[bottomIndex].a > alphaThresholdByte)
                            {
                                visited[bottomIndex] = true;
                                stack.Add(bottomIndex);
                            }
                        }
                        if (currentY < textureHeight - 1)
                        {
                            int topIndex = current + textureWidth;
                            if (!visited[topIndex] && pixels[topIndex].a > alphaThresholdByte)
                            {
                                visited[topIndex] = true;
                                stack.Add(topIndex);
                            }
                        }
                    }

                    int width = maxX - minX + 1;
                    int height = maxY - minY + 1;

                    if (width >= 2 && height >= 2)
                    {
                        result.Add(new Rect(minX, minY, width, height));
                    }
                }
            }

            if (result.Count > 1)
            {
                result.Sort(
                    (a, b) =>
                    {
                        int yCompare = b.y.CompareTo(a.y);
                        return yCompare != 0 ? yCompare : a.x.CompareTo(b.x);
                    }
                );
            }
        }

        internal void ExtractSelectedSprites()
        {
            if (_discoveredSheets == null || _discoveredSheets.Count == 0)
            {
                this.LogWarn($"No sprite sheets discovered.");
                return;
            }

            string outputPath = AssetDatabase.GetAssetPath(_outputDirectory);
            if (string.IsNullOrWhiteSpace(outputPath) || !AssetDatabase.IsValidFolder(outputPath))
            {
                this.LogError($"Invalid output directory.");
                return;
            }

            _lastExtractedCount = 0;
            _lastSkippedCount = 0;
            _lastErrorCount = 0;

            bool canceled = false;
            int totalSprites = 0;
            int processedSprites = 0;

            for (int i = 0; i < _discoveredSheets.Count; ++i)
            {
                SpriteSheetEntry entry = _discoveredSheets[i];
                if (!entry._isSelected || entry._sprites == null)
                {
                    continue;
                }
                for (int j = 0; j < entry._sprites.Count; ++j)
                {
                    if (entry._sprites[j]._isSelected)
                    {
                        ++totalSprites;
                    }
                }
            }

            if (totalSprites == 0)
            {
                this.LogWarn($"No sprites selected for extraction.");
                return;
            }

            WallstopGenericPool<Dictionary<string, bool>> originalReadablePool = DictionaryBuffer<
                string,
                bool
            >.GetDictionaryPool(StringComparer.OrdinalIgnoreCase);
            using PooledResource<Dictionary<string, bool>> originalReadableLease =
                originalReadablePool.Get(out Dictionary<string, bool> originalReadable);

            // Use pooled list for pending imports to batch all import operations
            using PooledResource<List<PendingImportSettings>> pendingImportsLease =
                Buffers<PendingImportSettings>.List.Get(
                    out List<PendingImportSettings> pendingImports
                );

            // Use pooled list for paths to batch import
            using PooledResource<List<string>> pendingPathsLease = Buffers<string>.List.Get(
                out List<string> pendingPaths
            );

            try
            {
                // Phase 1: Make source textures readable (batched)
                using (AssetDatabaseBatchHelper.BeginBatch())
                {
                    for (int i = 0; i < _discoveredSheets.Count && !canceled; ++i)
                    {
                        SpriteSheetEntry entry = _discoveredSheets[i];
                        if (!entry._isSelected || entry._sprites == null)
                        {
                            continue;
                        }

                        if (
                            Utils.EditorUi.CancelableProgress(
                                Name,
                                $"Making readable: {Path.GetFileName(entry._assetPath)}",
                                (float)i / _discoveredSheets.Count * 0.05f
                            )
                        )
                        {
                            canceled = true;
                            break;
                        }

                        if (!entry._importer.isReadable)
                        {
                            originalReadable[entry._assetPath] = false;
                            entry._importer.isReadable = true;
                            entry._importer.SaveAndReimport();
                        }
                        else
                        {
                            originalReadable[entry._assetPath] = true;
                        }
                    }
                }

                AssetDatabase.SaveAssets();

                // Phase 2a: Write all PNG files (no imports yet)
                if (!canceled)
                {
                    for (int i = 0; i < _discoveredSheets.Count && !canceled; ++i)
                    {
                        SpriteSheetEntry entry = _discoveredSheets[i];
                        if (!entry._isSelected || entry._sprites == null)
                        {
                            continue;
                        }

                        entry._texture = AssetDatabase.LoadAssetAtPath<Texture2D>(entry._assetPath);
                        if (entry._texture == null)
                        {
                            this.LogWarn($"Failed to reload texture: {entry._assetPath}");
                            continue;
                        }

                        List<SpriteEntryData> sortedSprites = GetSortedSprites(entry._sprites);

                        for (int j = 0; j < sortedSprites.Count && !canceled; ++j)
                        {
                            SpriteEntryData sprite = sortedSprites[j];
                            if (!sprite._isSelected)
                            {
                                continue;
                            }

                            if (
                                Utils.EditorUi.CancelableProgress(
                                    Name,
                                    $"Writing: {sprite._originalName}",
                                    0.05f + (float)processedSprites / totalSprites * 0.35f
                                )
                            )
                            {
                                canceled = true;
                                break;
                            }

                            string extractedPath = ExtractSpriteDeferred(
                                entry,
                                sprite,
                                outputPath,
                                j,
                                pendingImports
                            );
                            if (extractedPath != null)
                            {
                                pendingPaths.Add(extractedPath);
                                ++_lastExtractedCount;
                            }
                            ++processedSprites;
                        }
                    }
                }

                // Phase 2b: Batch import all extracted sprites
                if (!canceled && pendingPaths.Count > 0)
                {
                    using (AssetDatabaseBatchHelper.BeginBatch())
                    {
                        for (int i = 0; i < pendingPaths.Count; ++i)
                        {
                            if (
                                Utils.EditorUi.CancelableProgress(
                                    Name,
                                    $"Importing: {Path.GetFileName(pendingPaths[i])}",
                                    0.4f + (float)i / pendingPaths.Count * 0.3f
                                )
                            )
                            {
                                canceled = true;
                                break;
                            }

                            AssetDatabase.ImportAsset(pendingPaths[i]);
                        }
                    }
                }

                // Phase 2c: Batch apply import settings (no per-file SaveAndReimport)
                if (!canceled && _preserveImportSettings && pendingImports.Count > 0)
                {
                    using (AssetDatabaseBatchHelper.BeginBatch())
                    {
                        for (int i = 0; i < pendingImports.Count; ++i)
                        {
                            PendingImportSettings pending = pendingImports[i];

                            if (
                                Utils.EditorUi.CancelableProgress(
                                    Name,
                                    $"Applying settings: {Path.GetFileName(pending.OutputPath)}",
                                    0.7f + (float)i / pendingImports.Count * 0.2f
                                )
                            )
                            {
                                canceled = true;
                                break;
                            }

                            ApplyImportSettingsDeferred(
                                pending.OutputPath,
                                pending.SourceImporter,
                                pending.Sprite,
                                pending.Entry
                            );
                        }
                    }
                }

                // Phase 3: Restore original readable state (batched)
                if (originalReadable.Count > 0)
                {
                    using PooledResource<List<string>> keysLease = Buffers<string>.List.Get(
                        out List<string> keys
                    );
                    foreach (KeyValuePair<string, bool> kvp in originalReadable)
                    {
                        keys.Add(kvp.Key);
                    }

                    using (AssetDatabaseBatchHelper.BeginBatch())
                    {
                        for (int keyIndex = 0; keyIndex < keys.Count; ++keyIndex)
                        {
                            string key = keys[keyIndex];
                            if (originalReadable[key])
                            {
                                continue;
                            }

                            if (
                                AssetImporter.GetAtPath(key) is TextureImporter
                                {
                                    textureType: TextureImporterType.Sprite
                                } imp
                            )
                            {
                                imp.isReadable = false;
                                imp.SaveAndReimport();
                            }
                        }
                    }
                }

                // Single SaveAssets call at end
                AssetDatabase.SaveAssets();

                if (canceled)
                {
                    this.LogWarn($"Extraction canceled by user.");
                }
                else
                {
                    string mode = _dryRun ? "Dry run" : "Extraction";
                    this.Log(
                        $"{mode} complete. Extracted: {_lastExtractedCount}, Skipped: {_lastSkippedCount}, Errors: {_lastErrorCount}"
                    );
                }
            }
            catch (Exception e)
            {
                this.LogError($"Error during extraction", e);
            }
            finally
            {
                Utils.EditorUi.ClearProgress();
            }
        }

        private bool ExtractSprite(
            SpriteSheetEntry sheet,
            SpriteEntryData sprite,
            string outputPath,
            int index
        )
        {
            if (sheet?._texture == null)
            {
                this.LogError($"Cannot extract sprite: texture is null.");
                ++_lastErrorCount;
                return false;
            }

            if (sprite == null)
            {
                ++_lastErrorCount;
                return false;
            }

            try
            {
                string prefix = string.IsNullOrWhiteSpace(_namingPrefix)
                    ? Path.GetFileNameWithoutExtension(sheet._assetPath)
                    : _namingPrefix;

                if (string.IsNullOrWhiteSpace(prefix))
                {
                    this.LogError(
                        $"Cannot extract sprite '{sprite._originalName}': output filename prefix is empty."
                    );
                    ++_lastErrorCount;
                    return false;
                }

                string outputFileName = $"{prefix}_{index:D3}.png";
                string fullOutputPath = Path.Combine(outputPath, outputFileName);

                if (!_overwriteExisting && File.Exists(fullOutputPath))
                {
                    ++_lastSkippedCount;
                    return false;
                }

                if (_dryRun)
                {
                    this.Log($"Would extract: {sprite._originalName} -> {fullOutputPath}");
                    return true;
                }

                int x = Mathf.FloorToInt(sprite._rect.x);
                int y = Mathf.FloorToInt(sprite._rect.y);
                int width = Mathf.FloorToInt(sprite._rect.width);
                int height = Mathf.FloorToInt(sprite._rect.height);

                x = Mathf.Clamp(x, 0, sheet._texture.width - 1);
                y = Mathf.Clamp(y, 0, sheet._texture.height - 1);
                width = Mathf.Clamp(width, 1, sheet._texture.width - x);
                height = Mathf.Clamp(height, 1, sheet._texture.height - y);

                if (!IsTextureFormatSupportedForGetPixels(sheet._texture.format))
                {
                    this.LogError(
                        $"Texture format '{sheet._texture.format}' does not support GetPixels32 for {sheet._assetPath}. Extraction skipped."
                    );
                    ++_lastErrorCount;
                    return false;
                }

                Color32[] pixels = sheet._texture.GetPixels32();
                int srcWidth = sheet._texture.width;
                int pixelCount = width * height;
                // Note: Cannot use pooled arrays here because SetPixels32 requires the array length
                // to exactly match the texture dimensions, but ArrayPool returns arrays that may be
                // larger than requested.
                Color32[] destPixels = new Color32[pixelCount];

                Parallel.For(
                    0,
                    height,
                    destY =>
                    {
                        int srcY = y + destY;
                        int destRowStart = destY * width;
                        int srcRowStart = srcY * srcWidth + x;
                        for (int destX = 0; destX < width; ++destX)
                        {
                            destPixels[destRowStart + destX] = pixels[srcRowStart + destX];
                        }
                    }
                );

                Texture2D extracted = null;
                try
                {
                    extracted = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    extracted.SetPixels32(destPixels);
                    extracted.Apply();

                    byte[] pngBytes = extracted.EncodeToPNG();
                    File.WriteAllBytes(fullOutputPath, pngBytes);
                }
                finally
                {
                    if (extracted != null)
                    {
                        DestroyImmediate(extracted);
                    }
                }

                AssetDatabase.ImportAsset(fullOutputPath);

                if (_preserveImportSettings)
                {
                    ApplyImportSettings(fullOutputPath, sheet._importer, sprite, sheet);
                }

                return true;
            }
            catch (Exception e)
            {
                this.LogError($"Failed to extract sprite '{sprite._originalName}'", e);
                ++_lastErrorCount;
                return false;
            }
        }

        /// <summary>
        /// Extracts a sprite to a PNG file without importing it. This is the deferred variant
        /// that writes the file to disk but does NOT call AssetDatabase.ImportAsset or ApplyImportSettings.
        /// </summary>
        /// <param name="sheet">The sprite sheet entry containing the source texture.</param>
        /// <param name="sprite">The sprite data to extract.</param>
        /// <param name="outputPath">The output directory path.</param>
        /// <param name="index">The sprite index for naming.</param>
        /// <param name="pendingImports">List to add pending import settings if _preserveImportSettings is true.</param>
        /// <returns>The full output path if successful, null if skipped or failed.</returns>
        private string ExtractSpriteDeferred(
            SpriteSheetEntry sheet,
            SpriteEntryData sprite,
            string outputPath,
            int index,
            List<PendingImportSettings> pendingImports
        )
        {
            if (sheet?._texture == null)
            {
                this.LogError($"Cannot extract sprite: texture is null.");
                ++_lastErrorCount;
                return null;
            }

            if (sprite == null)
            {
                ++_lastErrorCount;
                return null;
            }

            try
            {
                string prefix = string.IsNullOrWhiteSpace(_namingPrefix)
                    ? Path.GetFileNameWithoutExtension(sheet._assetPath)
                    : _namingPrefix;

                if (string.IsNullOrWhiteSpace(prefix))
                {
                    this.LogError(
                        $"Cannot extract sprite '{sprite._originalName}': output filename prefix is empty."
                    );
                    ++_lastErrorCount;
                    return null;
                }

                string outputFileName = $"{prefix}_{index:D3}.png";
                string fullOutputPath = Path.Combine(outputPath, outputFileName);

                if (!_overwriteExisting && File.Exists(fullOutputPath))
                {
                    ++_lastSkippedCount;
                    return null;
                }

                if (_dryRun)
                {
                    this.Log($"Would extract: {sprite._originalName} -> {fullOutputPath}");
                    // Return path for dry run to count as "success" but don't add to pending imports
                    return fullOutputPath;
                }

                int x = Mathf.FloorToInt(sprite._rect.x);
                int y = Mathf.FloorToInt(sprite._rect.y);
                int width = Mathf.FloorToInt(sprite._rect.width);
                int height = Mathf.FloorToInt(sprite._rect.height);

                x = Mathf.Clamp(x, 0, sheet._texture.width - 1);
                y = Mathf.Clamp(y, 0, sheet._texture.height - 1);
                width = Mathf.Clamp(width, 1, sheet._texture.width - x);
                height = Mathf.Clamp(height, 1, sheet._texture.height - y);

                if (!IsTextureFormatSupportedForGetPixels(sheet._texture.format))
                {
                    this.LogError(
                        $"Texture format '{sheet._texture.format}' does not support GetPixels32 for {sheet._assetPath}. Extraction skipped."
                    );
                    ++_lastErrorCount;
                    return null;
                }

                Color32[] pixels = sheet._texture.GetPixels32();
                int srcWidth = sheet._texture.width;
                int pixelCount = width * height;
                // Note: Cannot use pooled arrays here because SetPixels32 requires the array length
                // to exactly match the texture dimensions, but ArrayPool returns arrays that may be
                // larger than requested.
                Color32[] destPixels = new Color32[pixelCount];

                Parallel.For(
                    0,
                    height,
                    destY =>
                    {
                        int srcY = y + destY;
                        int destRowStart = destY * width;
                        int srcRowStart = srcY * srcWidth + x;
                        for (int destX = 0; destX < width; ++destX)
                        {
                            destPixels[destRowStart + destX] = pixels[srcRowStart + destX];
                        }
                    }
                );

                Texture2D extracted = null;
                try
                {
                    extracted = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    extracted.SetPixels32(destPixels);
                    extracted.Apply();

                    byte[] pngBytes = extracted.EncodeToPNG();
                    File.WriteAllBytes(fullOutputPath, pngBytes);
                }
                finally
                {
                    if (extracted != null)
                    {
                        DestroyImmediate(extracted);
                    }
                }

                // Add to pending imports if _preserveImportSettings is true
                // Note: We don't call ImportAsset or ApplyImportSettings here - that's done in batch later
                if (_preserveImportSettings)
                {
                    pendingImports.Add(
                        new PendingImportSettings(fullOutputPath, sheet._importer, sprite, sheet)
                    );
                }

                return fullOutputPath;
            }
            catch (Exception e)
            {
                this.LogError($"Failed to extract sprite '{sprite._originalName}'", e);
                ++_lastErrorCount;
                return null;
            }
        }

        private void ApplyImportSettings(
            string outputPath,
            TextureImporter sourceImporter,
            SpriteEntryData sprite,
            SpriteSheetEntry entry
        )
        {
            if (AssetImporter.GetAtPath(outputPath) is not TextureImporter newImporter)
            {
                return;
            }

            newImporter.textureType = TextureImporterType.Sprite;
            newImporter.spriteImportMode = SpriteImportMode.Single;
            newImporter.spritePixelsPerUnit = sourceImporter.spritePixelsPerUnit;
            newImporter.filterMode = sourceImporter.filterMode;
            newImporter.textureCompression = sourceImporter.textureCompression;
            newImporter.wrapMode = sourceImporter.wrapMode;
            newImporter.mipmapEnabled = sourceImporter.mipmapEnabled;
            newImporter.isReadable = sourceImporter.isReadable;

            // Resolve pivot using the cascade: per-sprite -> per-sheet -> global settings
            Vector2 pivot = GetEffectivePivot(entry, sprite);

            TextureImporterSettings settings = new();
            newImporter.ReadTextureSettings(settings);
            settings.spritePivot = pivot;
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spriteBorder = sprite._border;
            newImporter.SetTextureSettings(settings);
            newImporter.spritePivot = pivot;

            try
            {
                TextureImporterPlatformSettings srcDefault =
                    sourceImporter.GetDefaultPlatformTextureSettings();
                if (!string.IsNullOrWhiteSpace(srcDefault.name))
                {
                    newImporter.SetPlatformTextureSettings(srcDefault);
                }
            }
            catch (Exception e)
            {
                this.LogWarn($"Failed to copy platform settings for '{outputPath}'", e);
            }

            newImporter.SaveAndReimport();
        }

        /// <summary>
        /// Applies import settings to an extracted sprite without calling SaveAndReimport.
        /// This is the deferred variant that copies settings and marks the importer dirty,
        /// allowing the batch scope to handle the reimport.
        /// </summary>
        /// <param name="outputPath">The asset path of the extracted sprite.</param>
        /// <param name="sourceImporter">The source texture importer to copy settings from.</param>
        /// <param name="sprite">The sprite entry data containing pivot, border, and other settings.</param>
        /// <param name="entry">The parent sheet entry for additional context.</param>
        private void ApplyImportSettingsDeferred(
            string outputPath,
            TextureImporter sourceImporter,
            SpriteEntryData sprite,
            SpriteSheetEntry entry
        )
        {
            if (AssetImporter.GetAtPath(outputPath) is not TextureImporter newImporter)
            {
                return;
            }

            newImporter.textureType = TextureImporterType.Sprite;
            newImporter.spriteImportMode = SpriteImportMode.Single;
            newImporter.spritePixelsPerUnit = sourceImporter.spritePixelsPerUnit;
            newImporter.filterMode = sourceImporter.filterMode;
            newImporter.textureCompression = sourceImporter.textureCompression;
            newImporter.wrapMode = sourceImporter.wrapMode;
            newImporter.mipmapEnabled = sourceImporter.mipmapEnabled;
            newImporter.isReadable = sourceImporter.isReadable;

            // Resolve pivot using the cascade: per-sprite -> per-sheet -> global settings
            Vector2 pivot = GetEffectivePivot(entry, sprite);

            TextureImporterSettings settings = new();
            newImporter.ReadTextureSettings(settings);
            settings.spritePivot = pivot;
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spriteBorder = sprite._border;
            newImporter.SetTextureSettings(settings);
            newImporter.spritePivot = pivot;
            /*
                  TextureImporterSettings settings = new();
                            importer.ReadTextureSettings(settings);
                            settings.spritePivot = newPivot;
                            settings.spriteAlignment = (int)SpriteAlignment.Custom;
                            importer.SetTextureSettings(settings);
                            importer.spritePivot = newPivot;
                            importers.Add(importer);
             */

            try
            {
                TextureImporterPlatformSettings srcDefault =
                    sourceImporter.GetDefaultPlatformTextureSettings();
                if (!string.IsNullOrWhiteSpace(srcDefault.name))
                {
                    newImporter.SetPlatformTextureSettings(srcDefault);
                }
            }
            catch (Exception e)
            {
                this.LogWarn($"Failed to copy platform settings for '{outputPath}'", e);
            }

            // SaveAndReimport is required to apply import settings - EditorUtility.SetDirty does NOT
            // trigger an import. Even inside a batch scope, we need to call this to persist changes.
            newImporter.SaveAndReimport();
        }

        private void ReplaceSpriteReferences()
        {
            if (_discoveredSheets == null || _discoveredSheets.Count == 0)
            {
                this.LogWarn($"No sprite sheets discovered. Run 'Find Sprite Sheets' first.");
                return;
            }

            string outputPath = AssetDatabase.GetAssetPath(_outputDirectory);
            if (string.IsNullOrWhiteSpace(outputPath) || !AssetDatabase.IsValidFolder(outputPath))
            {
                this.LogError($"Invalid output directory for reference replacement.");
                return;
            }

            WallstopGenericPool<Dictionary<Sprite, Sprite>> mappingPool = DictionaryBuffer<
                Sprite,
                Sprite
            >.Dictionary;
            using PooledResource<Dictionary<Sprite, Sprite>> mappingLease = mappingPool.Get(
                out Dictionary<Sprite, Sprite> mapping
            );

            try
            {
                for (int i = 0; i < _discoveredSheets.Count; ++i)
                {
                    SpriteSheetEntry entry = _discoveredSheets[i];
                    if (!entry._isSelected || entry._sprites == null)
                    {
                        continue;
                    }

                    Object[] originalSprites = AssetDatabase.LoadAllAssetsAtPath(entry._assetPath);
                    List<SpriteEntryData> sortedSprites = GetSortedSprites(entry._sprites);

                    for (int j = 0; j < sortedSprites.Count; ++j)
                    {
                        SpriteEntryData spriteData = sortedSprites[j];
                        if (!spriteData._isSelected)
                        {
                            continue;
                        }

                        Sprite originalSprite = null;
                        for (int k = 0; k < originalSprites.Length; ++k)
                        {
                            if (
                                originalSprites[k] is Sprite s
                                && s.name == spriteData._originalName
                            )
                            {
                                originalSprite = s;
                                break;
                            }
                        }

                        if (originalSprite == null)
                        {
                            continue;
                        }

                        string prefix = string.IsNullOrWhiteSpace(_namingPrefix)
                            ? Path.GetFileNameWithoutExtension(entry._assetPath)
                            : _namingPrefix;
                        string extractedFileName = $"{prefix}_{j:D3}.png";
                        string extractedPath = Path.Combine(outputPath, extractedFileName);

                        Sprite extractedSprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                            extractedPath
                        );
                        if (extractedSprite != null)
                        {
                            mapping[originalSprite] = extractedSprite;
                        }
                    }
                }

                if (mapping.Count == 0)
                {
                    this.LogWarn($"No original->extracted sprite mappings found.");
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

                using (AssetDatabaseBatchHelper.BeginBatch())
                {
                    for (int i = 0; i < allAssets.Length; ++i)
                    {
                        string path = allAssets[i];
                        if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        bool hasValidExt = false;
                        for (int extIndex = 0; extIndex < candidateExts.Length; ++extIndex)
                        {
                            if (
                                path.EndsWith(
                                    candidateExts[extIndex],
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                            {
                                hasValidExt = true;
                                break;
                            }
                        }
                        if (!hasValidExt)
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
                        for (int objIndex = 0; objIndex < objs.Length; ++objIndex)
                        {
                            Object o = objs[objIndex];
                            if (o == null)
                            {
                                continue;
                            }

                            SerializedObject so = new(o);
                            SerializedProperty it = so.GetIterator();
                            bool enter = true;
                            while (it.NextVisible(enter))
                            {
                                enter = false;
                                if (it.propertyType != SerializedPropertyType.ObjectReference)
                                {
                                    continue;
                                }

                                Sprite s = it.objectReferenceValue as Sprite;
                                if (s != null && mapping.TryGetValue(s, out Sprite replacement))
                                {
                                    Undo.RecordObject(o, "Replace sprite reference");
                                    it.objectReferenceValue = replacement;
                                    assetModified = true;
                                    this.Log(
                                        $"Replaced reference in {path}: {s.name} -> {replacement.name}"
                                    );
                                }
                            }
                            if (assetModified)
                            {
                                so.ApplyModifiedPropertiesWithoutUndo();
                                EditorUtility.SetDirty(o);
                            }
                        }
                        if (assetModified)
                        {
                            ++modifiedAssets;
                        }
                    }
                }

                AssetDatabase.SaveAssets();
                Utils.EditorUi.ClearProgress();

                this.Log(
                    $"Reference replacement complete. Modified assets: {modifiedAssets}. Mapped pairs: {mapping.Count}."
                );
            }
            catch (Exception e)
            {
                this.LogError($"Error during reference replacement", e);
            }
        }
    }
#endif
}
