// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Text.RegularExpressions;
    using UnityEditor;
    using UnityEngine;
    using CustomEditors;
    using Utils;
    using WallstopStudios.UnityHelpers.Core.Animation;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Data class representing a single animation definition with frames, timing, and preview settings.
    /// </summary>
    [Serializable]
    public sealed class AnimationData
    {
        /// <summary>
        /// Default frames per second for new animations.
        /// </summary>
        public const float DefaultFramesPerSecond = 12;

        /// <summary>
        /// The sprite frames that make up this animation.
        /// </summary>
        public List<Sprite> frames = new();

        /// <summary>
        /// Constant frames per second (used when <see cref="framerateMode"/> is <see cref="FramerateMode.Constant"/>).
        /// </summary>
        public float framesPerSecond = DefaultFramesPerSecond;

        /// <summary>
        /// Name of the animation clip to be generated.
        /// </summary>
        public string animationName = string.Empty;

        /// <summary>
        /// Whether this animation data was created from auto-parsing.
        /// </summary>
        public bool isCreatedFromAutoParse;

        /// <summary>
        /// Whether the animation should loop.
        /// </summary>
        public bool loop;

        /// <summary>
        /// Determines how the framerate is calculated for each frame.
        /// </summary>
        public FramerateMode framerateMode = FramerateMode.Constant;

        /// <summary>
        /// AnimationCurve defining FPS over normalized animation progress (0-1).
        /// X-axis: normalized frame position (0 = first frame, 1 = last frame).
        /// Y-axis: frames per second at that position.
        /// Used when <see cref="framerateMode"/> is <see cref="FramerateMode.Curve"/>.
        /// </summary>
        public AnimationCurve framesPerSecondCurve = AnimationCurve.Constant(
            0f,
            1f,
            DefaultFramesPerSecond
        );

        /// <summary>
        /// Starting point in the animation loop (0-1). Applied to the generated clip settings.
        /// </summary>
        public float cycleOffset;

        /// <summary>
        /// Whether to show the live preview panel for this animation.
        /// This field is transient and not serialized to assets.
        /// </summary>
        [NonSerialized]
        public bool showPreview;
    }

    /// <summary>
    /// Builds AnimationClips from sprites using flexible grouping and naming rules. Supports
    /// auto-parsing by folders, regex-based grouping, duplicate-resolution, dry-run previews,
    /// optional case-insensitive grouping, variable framerate via AnimationCurve, and live animation preview.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Problems this solves: turning folder(s) of sprites into one or many consistent
    /// <see cref="AnimationClip"/> assets with predictable names and frame rates. The variable
    /// framerate feature allows creating animations with timing variations like attack windups,
    /// ease-in/ease-out effects, and dramatic pauses.
    /// </para>
    /// <para>
    /// How it works: choose directories and a sprite name regex; optionally supply custom group
    /// regex with named groups <c>base</c>/<c>index</c> or rely on common patterns
    /// (e.g., name_01, name (2), name3). Configure per-animation FPS or FPS curve, loop flag, and naming
    /// prefixes/suffixes. Use Calculate/Dry-Run sections to preview results before generating.
    /// Enable the preview panel to see live animation playback before saving.
    /// </para>
    /// <para>
    /// Pros: reproducible clip creation, battle-tested grouping heuristics, detailed previews,
    /// variable framerate support, live animation preview.
    /// Caveats: ensure regex correctness; strict numeric ordering can be toggled when mixed digits
    /// produce undesired lexicographic ordering.
    /// </para>
    /// </remarks>
    /// <example>
    /// <![CDATA[
    /// // Open via menu: Tools/Wallstop Studios/Unity Helpers/Animation Creator
    /// // Example filter: ^Enemy_(?<base>Walk)_(?<index>\d+)$
    /// // Add folders, enable "Resolve Duplicate Animation Names" to avoid conflicts,
    /// // configure Framerate Mode to "Curve" for variable timing,
    /// // then Generate to create .anim files under a chosen folder.
    /// ]]>
    /// </example>
    public sealed class AnimationCreatorWindow : EditorWindow
    {
        private static readonly char[] WhiteSpaceSplitters = { ' ', '\t', '\n', '\r' };

        private static readonly GUIContent AnimationNameContent = new("Animation Name");
        private static readonly GUIContent FramesContent = new("Frames");
        private static readonly GUIContent LoopContent = new("Loop");
        private static readonly GUIContent FramerateModeContent = new(
            "Framerate Mode",
            "Constant: Single FPS value\nCurve: Variable FPS over animation progress"
        );
        private static readonly GUIContent FpsContent = new("FPS");
        private static readonly GUIContent FpsCurveContent = new(
            "FPS Curve",
            "X: Frame progress (0-1), Y: FPS at that point"
        );
        private static readonly GUIContent CycleOffsetContent = new(
            "Cycle Offset",
            "Starting point in the animation loop (0-1)"
        );
        private static readonly GUIContent FlatButtonContent = new(
            "Flat",
            "Constant FPS throughout"
        );
        private static readonly GUIContent EaseInButtonContent = new(
            "Ease In",
            "Start slow, speed up"
        );
        private static readonly GUIContent EaseOutButtonContent = new(
            "Ease Out",
            "Start fast, slow down"
        );
        private static readonly GUIContent SyncButtonContent = new(
            "Sync",
            "Set curve to current constant FPS"
        );
        private static readonly GUIContent PreviewToggleContent = new(
            "Preview",
            "Show live animation preview"
        );

        private SerializedObject _serializedObject;
        private SerializedProperty _animationDataProp;
        private SerializedProperty _animationSourcesProp;
        private SerializedProperty _spriteNameRegexProp;
        private SerializedProperty _textProp;
        private SerializedProperty _autoRefreshProp;
        private SerializedProperty _groupingCaseInsensitiveProp;
        private SerializedProperty _includeFolderNameProp;
        private SerializedProperty _includeFullFolderPathProp;
        private SerializedProperty _autoParseNamePrefixProp;
        private SerializedProperty _autoParseNameSuffixProp;
        private SerializedProperty _useCustomGroupRegexProp;
        private SerializedProperty _customGroupRegexProp;
        private SerializedProperty _customGroupRegexIgnoreCaseProp;
        private SerializedProperty _resolveDuplicateNamesProp;
        private SerializedProperty _regexTestInputProp;
        private SerializedProperty _strictNumericOrderingProp;

        public List<AnimationData> animationData = new();
        public List<Object> animationSources = new();
        public string spriteNameRegex = ".*";
        public string text;
        public bool autoRefresh = true;
        public bool groupingCaseInsensitive = true;
        public bool includeFolderNameInAnimName;
        public bool includeFullFolderPathInAnimName;
        public string autoParseNamePrefix = string.Empty;
        public string autoParseNameSuffix = string.Empty;
        public bool useCustomGroupRegex;
        public string customGroupRegex = string.Empty;
        public bool customGroupRegexIgnoreCase = true;
        public bool resolveDuplicateAnimationNames = true;
        public string regexTestInput = string.Empty;
        public bool strictNumericOrdering = false;

        [HideInInspector]
        [SerializeField]
        private List<Sprite> _filteredSprites = new();
        private int _matchedSpriteCount;
        private int _unmatchedSpriteCount;
        private Regex _compiledRegex;
        private string _lastUsedRegex;
        private string _searchString = string.Empty;
        private Vector2 _scrollPosition;
        private string _errorMessage = string.Empty;
        private Regex _compiledGroupRegex;
        private string _lastGroupRegex;
        private string _groupRegexErrorMessage = string.Empty;
        private int _lastSourcesHash;
        private bool _animationDataIsExpanded = true;
        private bool _autoParsePreviewExpanded = false;
        private bool _autoParseDryRunExpanded = false;

        private readonly Stopwatch _previewTimer = new();
        private int _previewAnimationIndex = -1;
        private int _previewFrameIndex;
        private TimeSpan _lastPreviewTick;
        private bool _isPreviewPlaying;
        private readonly Dictionary<Sprite, Texture2D> _previewTextureCache = new();

        private readonly Dictionary<string, AnimationCreatorConfig> _loadedConfigs = new();
        private bool _configSectionExpanded = true;

        private bool _needsRefresh;
        private int _lastRefreshFrame = -1;
        private int _lastLayoutFrame = -1;
        private float _lastScrollY;
        private const float ScrollThresholdForRepaint = 1f;

        private readonly struct CachedElementProperties
        {
            public readonly SerializedProperty animationNameProp;
            public readonly SerializedProperty framesProp;
            public readonly SerializedProperty loopProp;
            public readonly SerializedProperty framerateModeProp;
            public readonly SerializedProperty fpsProp;
            public readonly SerializedProperty curveProp;
            public readonly SerializedProperty cycleOffsetProp;
            public readonly int arrayIndex;
            public readonly int frameCount;

            public CachedElementProperties(
                SerializedProperty elementProp,
                int index,
                int frameCount
            )
            {
                this.arrayIndex = index;
                this.frameCount = frameCount;
                animationNameProp = elementProp.FindPropertyRelative(
                    nameof(AnimationData.animationName)
                );
                framesProp = elementProp.FindPropertyRelative(nameof(AnimationData.frames));
                loopProp = elementProp.FindPropertyRelative(nameof(AnimationData.loop));
                framerateModeProp = elementProp.FindPropertyRelative(
                    nameof(AnimationData.framerateMode)
                );
                fpsProp = elementProp.FindPropertyRelative(nameof(AnimationData.framesPerSecond));
                curveProp = elementProp.FindPropertyRelative(
                    nameof(AnimationData.framesPerSecondCurve)
                );
                cycleOffsetProp = elementProp.FindPropertyRelative(
                    nameof(AnimationData.cycleOffset)
                );
            }
        }

        private readonly Dictionary<int, CachedElementProperties> _cachedElementProperties = new();
        private int _lastCacheFrame = -1;
        private int _animationDataPageIndex;
        private const int AnimationDataPageSize = 20;

        private sealed class AutoParsePreviewRecord
        {
            public string folder;
            public string baseName;
            public int count;
            public bool hasIndex;
        }

        private readonly List<AutoParsePreviewRecord> _autoParsePreview = new();

        private sealed class AutoParseDryRunRecord
        {
            public string folderPath;
            public string finalName;
            public string finalAssetPath;
            public int count;
            public bool hasIndex;
            public bool duplicateResolved;
        }

        private readonly List<AutoParseDryRunRecord> _autoParseDryRun = new();

        private static readonly Regex s_ParenIndexRegex = new(
            @"^(?<base>.*?)[\s]*\(\s*(?<index>\d+)\s*\)\s*$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant
        );
        private static readonly Regex s_SeparatorIndexRegex = new(
            @"^(?<base>.*?)[_\-\.\s]+(?<index>\d+)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant
        );
        private static readonly Regex s_TrailingIndexRegex = new(
            @"^(?<base>.*?)(?<index>\d+)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant
        );

        [MenuItem("Tools/Wallstop Studios/Unity Helpers/Animation Creator", priority = -3)]
        public static void ShowWindow()
        {
            GetWindow<AnimationCreatorWindow>("Animation Creator");
        }

        private void OnEnable()
        {
            _serializedObject = new SerializedObject(this);
            _animationDataProp = _serializedObject.FindProperty(nameof(animationData));
            _animationSourcesProp = _serializedObject.FindProperty(nameof(animationSources));
            _spriteNameRegexProp = _serializedObject.FindProperty(nameof(spriteNameRegex));
            _textProp = _serializedObject.FindProperty(nameof(text));
            _autoRefreshProp = _serializedObject.FindProperty(nameof(autoRefresh));
            _groupingCaseInsensitiveProp = _serializedObject.FindProperty(
                nameof(groupingCaseInsensitive)
            );
            _includeFolderNameProp = _serializedObject.FindProperty(
                nameof(includeFolderNameInAnimName)
            );
            _includeFullFolderPathProp = _serializedObject.FindProperty(
                nameof(includeFullFolderPathInAnimName)
            );
            _autoParseNamePrefixProp = _serializedObject.FindProperty(nameof(autoParseNamePrefix));
            _autoParseNameSuffixProp = _serializedObject.FindProperty(nameof(autoParseNameSuffix));
            _useCustomGroupRegexProp = _serializedObject.FindProperty(nameof(useCustomGroupRegex));
            _customGroupRegexProp = _serializedObject.FindProperty(nameof(customGroupRegex));
            _customGroupRegexIgnoreCaseProp = _serializedObject.FindProperty(
                nameof(customGroupRegexIgnoreCase)
            );
            _resolveDuplicateNamesProp = _serializedObject.FindProperty(
                nameof(resolveDuplicateAnimationNames)
            );
            _regexTestInputProp = _serializedObject.FindProperty(nameof(regexTestInput));
            _strictNumericOrderingProp = _serializedObject.FindProperty(
                nameof(strictNumericOrdering)
            );

            UpdateRegex();
            UpdateGroupRegex();
            FindAndFilterSprites();
            _lastSourcesHash = ComputeSourcesHash();
            _needsRefresh = false;
            _lastRefreshFrame = Time.frameCount;

            TryAutoLoadConfigs();

            _previewTimer.Start();
            EditorApplication.update += OnPreviewUpdate;

            Repaint();
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnPreviewUpdate;
            _previewTimer.Stop();
            _previewAnimationIndex = -1;
            _previewTextureCache.Clear();
            _cachedElementProperties.Clear();
            _lastCacheFrame = -1;
        }

        private void OnPreviewUpdate()
        {
            if (!_isPreviewPlaying || _previewAnimationIndex < 0)
            {
                return;
            }

            if (_previewAnimationIndex >= animationData.Count)
            {
                StopPreview();
                return;
            }

            AnimationData data = animationData[_previewAnimationIndex];
            if (data.frames.Count == 0)
            {
                return;
            }

            float targetFps = GetCurrentFps(data, _previewFrameIndex);
            if (targetFps <= 0)
            {
                targetFps = AnimationData.DefaultFramesPerSecond;
            }

            TimeSpan frameDuration = TimeSpan.FromMilliseconds(1000.0 / targetFps);
            TimeSpan elapsed = _previewTimer.Elapsed;

            if (elapsed - _lastPreviewTick > frameDuration + frameDuration)
            {
                _lastPreviewTick = elapsed - frameDuration;
            }

            if (_lastPreviewTick + frameDuration > elapsed)
            {
                return;
            }

            _lastPreviewTick += frameDuration;

            int nextFrame = _previewFrameIndex + 1;
            if (nextFrame >= data.frames.Count)
            {
                if (data.loop)
                {
                    nextFrame = 0;
                }
                else
                {
                    StopPreview();
                    return;
                }
            }

            _previewFrameIndex = nextFrame;
            Repaint();
        }

        private void OnGUI()
        {
            _serializedObject.Update();

            EventType currentEventType = Event.current.type;
            bool isLayoutEvent = currentEventType == EventType.Layout;
            bool isRepaintEvent = currentEventType == EventType.Repaint;
            int currentFrame = Time.frameCount;

            if (isLayoutEvent && currentFrame != _lastLayoutFrame)
            {
                _lastLayoutFrame = currentFrame;

                if (autoRefresh)
                {
                    bool regexChanged = _compiledRegex == null || _lastUsedRegex != spriteNameRegex;
                    int currentSourcesHash = ComputeSourcesHash();
                    bool sourcesChanged = currentSourcesHash != _lastSourcesHash;

                    if (regexChanged)
                    {
                        UpdateRegex();
                        _needsRefresh = true;
                    }

                    if (sourcesChanged)
                    {
                        _lastSourcesHash = currentSourcesHash;
                        _needsRefresh = true;
                        TryAutoLoadConfigs();
                    }

                    if (
                        _compiledGroupRegex == null
                        || _lastGroupRegex != customGroupRegex
                        || customGroupRegexIgnoreCase
                            != ((_compiledGroupRegex?.Options & RegexOptions.IgnoreCase) != 0)
                    )
                    {
                        UpdateGroupRegex();
                    }
                }

                if (_needsRefresh && currentFrame != _lastRefreshFrame)
                {
                    _lastRefreshFrame = currentFrame;
                    _needsRefresh = false;
                    FindAndFilterSprites();
                }

                if (currentFrame != _lastCacheFrame)
                {
                    _cachedElementProperties.Clear();
                    _lastCacheFrame = currentFrame;
                }
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
            PersistentDirectoryGUI.PathSelectorObjectArray(
                _animationSourcesProp,
                nameof(AnimationCreatorWindow)
            );
            EditorGUILayout.PropertyField(_spriteNameRegexProp);
            EditorGUILayout.PropertyField(_textProp);
            EditorGUILayout.PropertyField(_autoRefreshProp, new GUIContent("Auto Refresh Filter"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Grouping & Naming", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(
                _groupingCaseInsensitiveProp,
                new GUIContent("Case-Insensitive Grouping")
            );
            EditorGUILayout.PropertyField(
                _includeFolderNameProp,
                new GUIContent("Prefix Leaf Folder Name")
            );
            EditorGUILayout.PropertyField(
                _includeFullFolderPathProp,
                new GUIContent("Prefix Full Folder Path")
            );
            EditorGUILayout.PropertyField(
                _autoParseNamePrefixProp,
                new GUIContent("Auto-Parse Name Prefix")
            );
            EditorGUILayout.PropertyField(
                _autoParseNameSuffixProp,
                new GUIContent("Auto-Parse Name Suffix")
            );
            EditorGUILayout.PropertyField(
                _resolveDuplicateNamesProp,
                new GUIContent("Resolve Duplicate Animation Names")
            );
            EditorGUILayout.PropertyField(
                _strictNumericOrderingProp,
                new GUIContent("Strict Numeric Ordering")
            );

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Custom Group Regex", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(
                _useCustomGroupRegexProp,
                new GUIContent("Enable Custom Group Regex")
            );
            using (new EditorGUI.DisabledScope(!_useCustomGroupRegexProp.boolValue))
            {
                EditorGUILayout.PropertyField(
                    _customGroupRegexProp,
                    new GUIContent("Pattern (?<base>)(?<index>)")
                );
                EditorGUILayout.PropertyField(
                    _customGroupRegexIgnoreCaseProp,
                    new GUIContent("Ignore Case (Regex)")
                );
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Regex Tester", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_regexTestInputProp, new GUIContent("Test Input"));
            if (!string.IsNullOrEmpty(regexTestInput))
            {
                if (_compiledRegex != null)
                {
                    bool match = _compiledRegex.IsMatch(regexTestInput);
                    EditorGUILayout.LabelField("Filter Regex Match:", match ? "Yes" : "No");
                }
                else
                {
                    EditorGUILayout.LabelField("Filter Regex Match:", "Invalid Pattern");
                }

                if (useCustomGroupRegex)
                {
                    if (_compiledGroupRegex != null)
                    {
                        Match m = _compiledGroupRegex.Match(regexTestInput);
                        if (m.Success)
                        {
                            string b = m.Groups["base"].Success ? m.Groups["base"].Value : "";
                            string idx = m.Groups["index"].Success ? m.Groups["index"].Value : "";
                            EditorGUILayout.LabelField("Custom Group Base:", b);
                            EditorGUILayout.LabelField(
                                "Custom Group Index:",
                                string.IsNullOrEmpty(idx) ? "(none)" : idx
                            );
                        }
                        else
                        {
                            EditorGUILayout.LabelField("Custom Group Result:", "No match");
                        }
                    }
                    else
                    {
                        EditorGUILayout.LabelField("Custom Group Result:", "Invalid Pattern");
                    }
                }

                if (TryExtractBaseAndIndex(regexTestInput, out string fbBase, out int fbIndex))
                {
                    EditorGUILayout.LabelField("Fallback Base:", fbBase);
                    EditorGUILayout.LabelField(
                        "Fallback Index:",
                        fbIndex >= 0 ? fbIndex.ToString() : "(none)"
                    );
                }
                else
                {
                    EditorGUILayout.LabelField("Fallback Result:", "No index; base = input");
                }
            }

            if (!string.IsNullOrWhiteSpace(_errorMessage))
            {
                EditorGUILayout.HelpBox(_errorMessage, MessageType.Error);
            }
            if (!string.IsNullOrWhiteSpace(_groupRegexErrorMessage))
            {
                EditorGUILayout.HelpBox(_groupRegexErrorMessage, MessageType.Error);
            }
            else if (
                _animationSourcesProp.arraySize == 0
                || animationSources.TrueForAll(val => Objects.Null(val))
            )
            {
                EditorGUILayout.HelpBox(
                    "Please specify at least one Animation Source (folder).",
                    MessageType.Error
                );
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Animation Data", EditorStyles.boldLabel);
            _searchString = EditorGUILayout.TextField("Search Animation Name", _searchString);

            DrawFilteredAnimationData();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Sprite Filter Status", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Regex Pattern:", spriteNameRegex);
            EditorGUILayout.LabelField("Matched Sprites:", _matchedSpriteCount.ToString());
            EditorGUILayout.LabelField("Unmatched Sprites:", _unmatchedSpriteCount.ToString());

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

            DrawActionButtons();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Auto-Parse Preview", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(_filteredSprites.Count == 0))
            {
                if (GUILayout.Button("Generate Auto-Parse Preview"))
                {
                    GenerateAutoParsePreview();
                    _autoParsePreviewExpanded = true;
                }
                if (GUILayout.Button("Generate Dry-Run Apply"))
                {
                    GenerateAutoParseDryRun();
                    _autoParseDryRunExpanded = true;
                }
            }
            if (_filteredSprites.Count == 0)
            {
                EditorGUILayout.HelpBox("No matched sprites to preview.", MessageType.Info);
            }
            _autoParsePreviewExpanded = EditorGUILayout.Foldout(
                _autoParsePreviewExpanded,
                $"Preview Groups ({_autoParsePreview.Count})",
                true
            );
            if (_autoParsePreviewExpanded && _autoParsePreview.Count > 0)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    int shown = 0;
                    foreach (AutoParsePreviewRecord rec in _autoParsePreview)
                    {
                        EditorGUILayout.LabelField(
                            $"{rec.folder} / {rec.baseName}",
                            $"Frames: {rec.count} | Numeric: {(rec.hasIndex ? "Yes" : "No")}"
                        );
                        shown++;
                        if (shown >= 200)
                        {
                            EditorGUILayout.LabelField($"Showing first {shown} groups...");
                            break;
                        }
                    }
                }
            }
            _autoParseDryRunExpanded = EditorGUILayout.Foldout(
                _autoParseDryRunExpanded,
                $"Dry-Run Results ({_autoParseDryRun.Count})",
                true
            );
            if (_autoParseDryRunExpanded && _autoParseDryRun.Count > 0)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    int shown = 0;
                    foreach (AutoParseDryRunRecord rec in _autoParseDryRun)
                    {
                        string info =
                            $"Name: {rec.finalName} | Frames: {rec.count} | Numeric: {(rec.hasIndex ? "Yes" : "No")}";
                        if (rec.duplicateResolved)
                        {
                            info += " | Renamed to avoid duplicate";
                        }
                        EditorGUILayout.LabelField(rec.folderPath, info);
                        EditorGUILayout.LabelField("-> Asset Path:", rec.finalAssetPath);
                        shown++;
                        if (shown >= 200)
                        {
                            EditorGUILayout.LabelField($"Showing first {shown} results...");
                            break;
                        }
                    }
                }
            }

            EditorGUILayout.EndScrollView();

            _ = _serializedObject.ApplyModifiedProperties();
        }

        private void DrawCheckSpritesButton()
        {
            if (GUILayout.Button("Check/Refresh Filtered Sprites"))
            {
                UpdateRegex();
                FindAndFilterSprites();
                Repaint();
            }
        }

        private void DrawFilteredAnimationData()
        {
            int listSize = _animationDataProp.arraySize;
            string[] searchTerms = string.IsNullOrWhiteSpace(_searchString)
                ? Array.Empty<string>()
                : _searchString.Split(WhiteSpaceSplitters, StringSplitOptions.RemoveEmptyEntries);

            using PooledResource<List<int>> matchingIndicesLease = Buffers<int>.List.Get(
                out List<int> matchingIndices
            );
            {
                for (int i = 0; i < listSize; ++i)
                {
                    SerializedProperty elementProp = _animationDataProp.GetArrayElementAtIndex(i);
                    SerializedProperty nameProp = elementProp.FindPropertyRelative(
                        nameof(AnimationData.animationName)
                    );

                    string currentName =
                        nameProp != null ? nameProp.stringValue ?? string.Empty : string.Empty;

                    bool matchesSearch = true;
                    if (searchTerms.Length > 0)
                    {
                        for (int si = 0; si < searchTerms.Length; si++)
                        {
                            if (
                                currentName.IndexOf(
                                    searchTerms[si],
                                    StringComparison.OrdinalIgnoreCase
                                ) < 0
                            )
                            {
                                matchesSearch = false;
                                break;
                            }
                        }
                    }

                    if (matchesSearch)
                    {
                        matchingIndices.Add(i);
                    }
                }
                int matchCount = matchingIndices.Count;
                string foldoutLabel =
                    $"{_animationDataProp.displayName} (Showing {matchCount} / {listSize})";
                _animationDataIsExpanded = EditorGUILayout.Foldout(
                    _animationDataIsExpanded,
                    foldoutLabel,
                    true
                );

                if (_animationDataIsExpanded)
                {
                    using EditorGUI.IndentLevelScope indent = new();
                    if (matchCount > 0)
                    {
                        int totalPages = Mathf.CeilToInt((float)matchCount / AnimationDataPageSize);
                        _animationDataPageIndex = Mathf.Clamp(
                            _animationDataPageIndex,
                            0,
                            totalPages - 1
                        );

                        if (totalPages > 1)
                        {
                            DrawAnimationDataPagination(matchCount, totalPages);
                        }

                        int startIndex = _animationDataPageIndex * AnimationDataPageSize;
                        int endIndex = Mathf.Min(startIndex + AnimationDataPageSize, matchCount);

                        for (int i = startIndex; i < endIndex; i++)
                        {
                            DrawAnimationDataElement(matchingIndices[i]);
                        }

                        if (totalPages > 1)
                        {
                            DrawAnimationDataPagination(matchCount, totalPages);
                        }
                    }
                    else if (listSize > 0)
                    {
                        EditorGUILayout.HelpBox(
                            $"No animation data matched the search term '{_searchString}'.",
                            MessageType.Info
                        );
                    }
                }
            }
        }

        private void DrawAnimationDataPagination(int totalItems, int totalPages)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(_animationDataPageIndex <= 0))
            {
                if (GUILayout.Button("<<", GUILayout.Width(30)))
                {
                    _animationDataPageIndex = 0;
                }
                if (GUILayout.Button("<", GUILayout.Width(30)))
                {
                    _animationDataPageIndex--;
                }
            }

            int startItem = _animationDataPageIndex * AnimationDataPageSize + 1;
            int endItem = Mathf.Min(startItem + AnimationDataPageSize - 1, totalItems);
            EditorGUILayout.LabelField(
                $"Page {_animationDataPageIndex + 1}/{totalPages} ({startItem}-{endItem} of {totalItems})",
                EditorStyles.centeredGreyMiniLabel,
                GUILayout.Width(180)
            );

            using (new EditorGUI.DisabledScope(_animationDataPageIndex >= totalPages - 1))
            {
                if (GUILayout.Button(">", GUILayout.Width(30)))
                {
                    _animationDataPageIndex++;
                }
                if (GUILayout.Button(">>", GUILayout.Width(30)))
                {
                    _animationDataPageIndex = totalPages - 1;
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private CachedElementProperties GetOrCreateCachedProperties(int index)
        {
            if (_cachedElementProperties.TryGetValue(index, out CachedElementProperties cached))
            {
                return cached;
            }

            SerializedProperty elementProp = _animationDataProp.GetArrayElementAtIndex(index);
            int frameCount = index < animationData.Count ? animationData[index].frames.Count : 0;
            CachedElementProperties newCached = new(elementProp, index, frameCount);
            _cachedElementProperties[index] = newCached;
            return newCached;
        }

        private void DrawAnimationDataElement(int index)
        {
            if (index < 0 || index >= animationData.Count)
            {
                return;
            }

            AnimationData data = animationData[index];
            CachedElementProperties props = GetOrCreateCachedProperties(index);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.PropertyField(props.animationNameProp, AnimationNameContent);

            EditorGUILayout.PropertyField(props.framesProp, FramesContent, true);

            EditorGUILayout.PropertyField(props.loopProp, LoopContent);

            EditorGUILayout.PropertyField(props.framerateModeProp, FramerateModeContent);

            FramerateMode currentMode = (FramerateMode)props.framerateModeProp.enumValueIndex;

            switch (currentMode)
            {
                case FramerateMode.Constant:
                    EditorGUILayout.PropertyField(props.fpsProp, FpsContent);
                    break;
                case FramerateMode.Curve:
                    DrawCurveFieldWithPresets(data, props, index);
                    break;
                default:
                    EditorGUILayout.PropertyField(props.fpsProp, FpsContent);
                    break;
            }

            EditorGUILayout.PropertyField(props.cycleOffsetProp, CycleOffsetContent);

            DrawPreviewPanel(data, index);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        private void DrawCurveFieldWithPresets(
            AnimationData data,
            CachedElementProperties props,
            int index
        )
        {
            EditorGUILayout.BeginHorizontal();

            SerializedProperty curveProp = props.curveProp;
            EditorGUILayout.PropertyField(curveProp, FpsCurveContent, GUILayout.MinWidth(200));

            EditorGUILayout.BeginVertical(GUILayout.Width(80));

            if (GUILayout.Button(FlatButtonContent))
            {
                float fps =
                    data.framesPerSecond > 0
                        ? data.framesPerSecond
                        : AnimationData.DefaultFramesPerSecond;
                data.framesPerSecondCurve = AnimationCurve.Constant(0, 1, fps);
                curveProp.animationCurveValue = data.framesPerSecondCurve;
            }

            if (GUILayout.Button(EaseInButtonContent))
            {
                data.framesPerSecondCurve = AnimationCurve.EaseInOut(0, 6, 1, 18);
                curveProp.animationCurveValue = data.framesPerSecondCurve;
            }

            if (GUILayout.Button(EaseOutButtonContent))
            {
                data.framesPerSecondCurve = AnimationCurve.EaseInOut(0, 18, 1, 6);
                curveProp.animationCurveValue = data.framesPerSecondCurve;
            }

            if (GUILayout.Button(SyncButtonContent))
            {
                data.framesPerSecondCurve = AnimationCurve.Constant(0, 1, data.framesPerSecond);
                curveProp.animationCurveValue = data.framesPerSecondCurve;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            DrawFrameTimingGraph(data);
        }

        private void DrawFrameTimingGraph(AnimationData data)
        {
            if (data.frames.Count < 2)
            {
                return;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Frame Timing Preview:", EditorStyles.boldLabel);

            using PooledResource<List<string>> _ = Buffers<string>.List.Get(
                out List<string> timings
            );
            float totalDuration = 0f;

            for (int i = 0; i < data.frames.Count; i++)
            {
                float normalizedPosition =
                    data.frames.Count > 1 ? (float)i / (data.frames.Count - 1) : 0f;

                float fps = data.framesPerSecondCurve.Evaluate(normalizedPosition);
                if (fps <= 0)
                {
                    fps =
                        data.framesPerSecond > 0
                            ? data.framesPerSecond
                            : AnimationData.DefaultFramesPerSecond;
                }

                float frameDuration = 1000f / fps;
                totalDuration += frameDuration;
                timings.Add($"F{i + 1}: {frameDuration:F0}ms ({fps:F1} fps)");
            }

            string timingText = string.Join(" | ", timings);
            EditorGUILayout.HelpBox(
                $"Total: {totalDuration:F0}ms ({totalDuration / 1000f:F2}s)\n{timingText}",
                MessageType.Info
            );
        }

        private void DrawPreviewPanel(AnimationData data, int animationIndex)
        {
            bool isThisAnimationPreviewing = _previewAnimationIndex == animationIndex;

            EditorGUILayout.BeginHorizontal();
            bool wantsPreview = GUILayout.Toggle(
                data.showPreview,
                PreviewToggleContent,
                "Button",
                GUILayout.Width(80)
            );

            if (wantsPreview != data.showPreview)
            {
                data.showPreview = wantsPreview;
                if (!wantsPreview && isThisAnimationPreviewing)
                {
                    StopPreview();
                }
                else if (wantsPreview)
                {
                    // Pre-load all preview textures only when preview is first enabled
                    // to avoid redundant texture loading on every frame (performance optimization)
                    foreach (Sprite spriteFrame in data.frames)
                    {
                        _ = GetPreviewTexture(spriteFrame);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            if (!data.showPreview || data.frames.Count == 0)
            {
                return;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            int displayFrameIndex = isThisAnimationPreviewing ? _previewFrameIndex : 0;
            displayFrameIndex = Mathf.Clamp(displayFrameIndex, 0, data.frames.Count - 1);
            Sprite currentSprite = data.frames[displayFrameIndex];

            if (currentSprite != null)
            {
                Texture2D preview = GetPreviewTexture(currentSprite);
                if (preview != null)
                {
                    float aspectRatio = (float)preview.width / preview.height;
                    float previewHeight = 128f;
                    float previewWidth = previewHeight * aspectRatio;

                    Rect previewRect = GUILayoutUtility.GetRect(
                        previewWidth,
                        previewHeight,
                        GUILayout.MaxWidth(256),
                        GUILayout.MaxHeight(128)
                    );
                    GUI.DrawTexture(previewRect, preview, ScaleMode.ScaleToFit);
                }
            }

            float currentFps = GetCurrentFps(data, displayFrameIndex);
            EditorGUILayout.LabelField(
                $"Frame: {displayFrameIndex + 1}/{data.frames.Count} | FPS: {currentFps:F1}",
                EditorStyles.centeredGreyMiniLabel
            );

            DrawTransportControls(data, animationIndex, isThisAnimationPreviewing);

            EditorGUI.BeginChangeCheck();
            float scrubberValue =
                data.frames.Count > 1 ? (float)displayFrameIndex / (data.frames.Count - 1) : 0f;

            float newScrubberValue = EditorGUILayout.Slider(scrubberValue, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                int newFrame = Mathf.RoundToInt(newScrubberValue * (data.frames.Count - 1));
                SetPreviewFrame(animationIndex, newFrame);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawTransportControls(AnimationData data, int animationIndex, bool isActive)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("|<", GUILayout.Width(30)))
            {
                SetPreviewFrame(animationIndex, 0);
            }

            if (GUILayout.Button("<", GUILayout.Width(30)))
            {
                int prev = isActive ? _previewFrameIndex - 1 : -1;
                if (prev < 0)
                {
                    prev = data.loop ? data.frames.Count - 1 : 0;
                }
                SetPreviewFrame(animationIndex, prev);
            }

            bool isPlaying = isActive && _isPreviewPlaying;
            if (GUILayout.Button(isPlaying ? "||" : ">", GUILayout.Width(40)))
            {
                if (isPlaying)
                {
                    PausePreview();
                }
                else
                {
                    StartPreview(animationIndex);
                }
            }

            if (GUILayout.Button(">", GUILayout.Width(30)))
            {
                int next = isActive ? _previewFrameIndex + 1 : 1;
                if (next >= data.frames.Count)
                {
                    next = data.loop ? 0 : data.frames.Count - 1;
                }
                SetPreviewFrame(animationIndex, next);
            }

            if (GUILayout.Button(">|", GUILayout.Width(30)))
            {
                SetPreviewFrame(animationIndex, data.frames.Count - 1);
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void StartPreview(int animationIndex)
        {
            if (_previewAnimationIndex >= 0 && _previewAnimationIndex < animationData.Count)
            {
                animationData[_previewAnimationIndex].showPreview = false;
            }

            _previewAnimationIndex = animationIndex;
            _previewFrameIndex = 0;
            _isPreviewPlaying = true;
            _lastPreviewTick = _previewTimer.Elapsed;

            if (animationIndex >= 0 && animationIndex < animationData.Count)
            {
                animationData[animationIndex].showPreview = true;
            }

            Repaint();
        }

        private void StopPreview()
        {
            _isPreviewPlaying = false;
            _previewAnimationIndex = -1;
            _previewFrameIndex = 0;
            Repaint();
        }

        private void PausePreview()
        {
            _isPreviewPlaying = false;
            Repaint();
        }

        private void SetPreviewFrame(int animationIndex, int frameIndex)
        {
            if (animationIndex < 0 || animationIndex >= animationData.Count)
            {
                return;
            }

            AnimationData data = animationData[animationIndex];
            if (data.frames.Count == 0)
            {
                return;
            }

            _previewAnimationIndex = animationIndex;
            _previewFrameIndex = Mathf.Clamp(frameIndex, 0, data.frames.Count - 1);
            _isPreviewPlaying = false;
            _lastPreviewTick = _previewTimer.Elapsed;

            if (!data.showPreview)
            {
                data.showPreview = true;
            }

            Repaint();
        }

        private Texture2D GetPreviewTexture(Sprite sprite)
        {
            if (sprite == null)
            {
                return null;
            }

            if (_previewTextureCache.TryGetValue(sprite, out Texture2D cached))
            {
                return cached;
            }

            Texture2D preview = AssetPreview.GetAssetPreview(sprite);
            if (preview != null)
            {
                _previewTextureCache[sprite] = preview;
            }

            return preview;
        }

        private float GetCurrentFps(AnimationData data, int frameIndex)
        {
            if (data.framerateMode == FramerateMode.Constant)
            {
                return data.framesPerSecond > 0
                    ? data.framesPerSecond
                    : AnimationData.DefaultFramesPerSecond;
            }

            float normalizedPosition =
                data.frames.Count > 1 ? (float)frameIndex / (data.frames.Count - 1) : 0f;

            float fps = data.framesPerSecondCurve.Evaluate(normalizedPosition);
            return fps > 0 ? fps : AnimationData.DefaultFramesPerSecond;
        }

        private void DrawActionButtons()
        {
            DrawCheckSpritesButton();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(_filteredSprites.Count == 0))
            {
                if (
                    GUILayout.Button(
                        $"Populate First Slot with {_filteredSprites.Count} Matched Sprites"
                    )
                )
                {
                    if (animationData.Count == 0)
                    {
                        this.LogWarn($"Add at least one Animation Data entry first.");
                    }
                    else if (animationData[0].frames.Count > 0)
                    {
                        if (
                            !Utils.EditorUi.Confirm(
                                "Confirm Overwrite",
                                "This will replace the frames currently in the first animation slot. Are you sure?",
                                "Replace",
                                "Cancel",
                                defaultWhenSuppressed: true
                            )
                        )
                        {
                            return;
                        }
                    }
                    if (animationData.Count > 0)
                    {
                        animationData[0].frames = new List<Sprite>(_filteredSprites);
                        animationData[0].animationName = "All_Matched_Sprites";
                        animationData[0].isCreatedFromAutoParse = false;
                        _serializedObject.Update();
                        Repaint();
                        this.Log($"Populated first slot with {_filteredSprites.Count} sprites.");
                    }
                }

                if (GUILayout.Button("Auto-Parse Matched Sprites into Animations"))
                {
                    if (
                        Utils.EditorUi.Confirm(
                            "Confirm Auto-Parse",
                            "This will replace the current animation list with animations generated from matched sprites based on their names (e.g., 'Player_Run_0', 'Player_Run_1'). Are you sure?",
                            "Parse",
                            "Cancel",
                            defaultWhenSuppressed: true
                        )
                    )
                    {
                        AutoParseSprites();
                        _serializedObject.Update();
                        Repaint();
                    }
                }
            }

            if (GUILayout.Button("Create new Animation Data"))
            {
                animationData.Add(new AnimationData());
                _serializedObject.Update();
                Repaint();
            }

            if (_filteredSprites.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Cannot perform sprite actions: No sprites matched the filter criteria or sources are empty.",
                    MessageType.Info
                );
            }

            bool canBulkName = animationData is { Count: > 0 } && !string.IsNullOrWhiteSpace(text);
            if (canBulkName)
            {
                bool anyFrames = false;
                for (int i = 0; i < animationData.Count; i++)
                {
                    List<Sprite> fr = animationData[i]?.frames;
                    if (fr is { Count: > 0 })
                    {
                        anyFrames = true;
                        break;
                    }
                }
                canBulkName = anyFrames;
            }

            using (new EditorGUI.DisabledScope(!canBulkName))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Bulk Naming Operations", EditorStyles.boldLabel);

                if (GUILayout.Button($"Append '{text}' To All Animation Names"))
                {
                    bool changed = false;
                    foreach (AnimationData data in animationData)
                    {
                        if (
                            !string.IsNullOrWhiteSpace(data.animationName)
                            && !data.animationName.EndsWith($"_{text}")
                        )
                        {
                            data.animationName += $"_{text}";
                            changed = true;
                        }
                    }
                    if (changed)
                    {
                        this.Log($"Appended '{text}' to animation names.");
                        _serializedObject.Update();
                        Repaint();
                    }
                    else
                    {
                        this.LogWarn(
                            $"No animation names modified. Either none exist or they already end with '_{text}'."
                        );
                    }
                }

                if (GUILayout.Button($"Remove '{text}' From End of Names"))
                {
                    bool changed = false;
                    string suffix = $"_{text}";
                    foreach (AnimationData data in animationData)
                    {
                        if (
                            !string.IsNullOrWhiteSpace(data.animationName)
                            && data.animationName.EndsWith(suffix)
                        )
                        {
                            data.animationName = data.animationName.Remove(
                                data.animationName.Length - suffix.Length
                            );
                            changed = true;
                        }
                        else if (
                            !string.IsNullOrWhiteSpace(data.animationName)
                            && data.animationName.EndsWith(text)
                        )
                        {
                            data.animationName = data.animationName.Remove(
                                data.animationName.Length - text.Length
                            );
                            changed = true;
                        }
                    }
                    if (changed)
                    {
                        this.Log($"Removed '{text}' suffix from animation names.");
                        _serializedObject.Update();
                        Repaint();
                    }
                    else
                    {
                        this.LogWarn(
                            $"No animation names modified. Either none exist or they do not end with '{text}' or '_{text}'."
                        );
                    }
                }
            }

            if (!canBulkName && animationData is { Count: > 0 })
            {
                bool anyFrames = false;
                for (int i = 0; i < animationData.Count; i++)
                {
                    List<Sprite> fr = animationData[i]?.frames;
                    if (fr is { Count: > 0 })
                    {
                        anyFrames = true;
                        break;
                    }
                }
                if (anyFrames)
                {
                    EditorGUILayout.HelpBox(
                        "Enter text in the 'Text' field above to enable bulk naming operations.",
                        MessageType.Info
                    );
                }
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(animationData is not { Count: > 0 }))
            {
                if (GUILayout.Button("Create Animations"))
                {
                    CreateAnimations();
                }
            }
            if (animationData is not { Count: > 0 })
            {
                EditorGUILayout.HelpBox(
                    "Add Animation Data entries before creating.",
                    MessageType.Warning
                );
            }

            DrawConfigurationPersistenceSection();
        }

        private void DrawConfigurationPersistenceSection()
        {
            EditorGUILayout.Space();
            _configSectionExpanded = EditorGUILayout.Foldout(
                _configSectionExpanded,
                "Configuration Persistence",
                true
            );

            if (!_configSectionExpanded)
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                bool hasValidSources =
                    animationSources != null
                    && animationSources.Exists(s =>
                        s != null && AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(s))
                    );

                using (new EditorGUI.DisabledScope(!hasValidSources))
                {
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Save Config to First Source"))
                    {
                        Object firstSource = animationSources.Find(s =>
                            s != null && AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(s))
                        );
                        if (firstSource != null)
                        {
                            string path = AssetDatabase.GetAssetPath(firstSource);
                            if (
                                Utils.EditorUi.Confirm(
                                    "Confirm Save",
                                    $"Save animation creator configuration to '{path}'?",
                                    "Save",
                                    "Cancel",
                                    defaultWhenSuppressed: true
                                )
                            )
                            {
                                SaveConfig(path);
                            }
                        }
                    }

                    if (GUILayout.Button("Save to All Sources"))
                    {
                        if (
                            Utils.EditorUi.Confirm(
                                "Confirm Save All",
                                "Save animation creator configuration to all source folders?",
                                "Save All",
                                "Cancel",
                                defaultWhenSuppressed: true
                            )
                        )
                        {
                            SaveAllConfigs();
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }

                List<string> foldersWithConfigs = GetFoldersWithConfigs();
                bool hasConfigs = foldersWithConfigs.Count > 0;

                using (new EditorGUI.DisabledScope(!hasConfigs))
                {
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Load Config from First Source"))
                    {
                        if (foldersWithConfigs.Count > 0)
                        {
                            if (
                                animationData.Count > 0
                                && !Utils.EditorUi.Confirm(
                                    "Confirm Load",
                                    "Loading configuration will replace current animation data. Continue?",
                                    "Load",
                                    "Cancel",
                                    defaultWhenSuppressed: true
                                )
                            )
                            {
                                return;
                            }

                            LoadConfig(foldersWithConfigs[0]);
                        }
                    }

                    if (GUILayout.Button("Reset to Default"))
                    {
                        if (
                            Utils.EditorUi.Confirm(
                                "Confirm Reset",
                                "Reset all settings to defaults? This will clear current animation data.",
                                "Reset",
                                "Cancel",
                                defaultWhenSuppressed: true
                            )
                        )
                        {
                            ResetToDefault();
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }

                if (hasConfigs)
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField(
                        $"Found configs in {foldersWithConfigs.Count} folder(s):",
                        EditorStyles.miniLabel
                    );
                    using (new EditorGUI.IndentLevelScope())
                    {
                        foreach (string folder in foldersWithConfigs)
                        {
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField(folder, EditorStyles.miniLabel);
                            if (GUILayout.Button("Load", GUILayout.Width(50)))
                            {
                                if (
                                    animationData.Count == 0
                                    || Utils.EditorUi.Confirm(
                                        "Confirm Load",
                                        $"Load configuration from '{folder}'? This will replace current animation data.",
                                        "Load",
                                        "Cancel",
                                        defaultWhenSuppressed: true
                                    )
                                )
                                {
                                    LoadConfig(folder);
                                }
                            }
                            if (GUILayout.Button("Delete", GUILayout.Width(50)))
                            {
                                if (
                                    Utils.EditorUi.Confirm(
                                        "Confirm Delete",
                                        $"Delete saved configuration from '{folder}'?",
                                        "Delete",
                                        "Cancel",
                                        defaultWhenSuppressed: false
                                    )
                                )
                                {
                                    DeleteConfig(folder);
                                }
                            }
                            EditorGUILayout.EndHorizontal();
                        }
                    }
                }
                else if (!hasValidSources)
                {
                    EditorGUILayout.HelpBox(
                        "Add valid folder sources to enable configuration persistence.",
                        MessageType.Info
                    );
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "No saved configurations found in source folders.",
                        MessageType.Info
                    );
                }
            }
        }

        private void CreateAnimations()
        {
            if (animationData is not { Count: > 0 })
            {
                this.LogError($"No animation data to create.");
                return;
            }

            string[] searchTerms = string.IsNullOrWhiteSpace(_searchString)
                ? Array.Empty<string>()
                : _searchString
                    .ToLowerInvariant()
                    .Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            using PooledResource<List<AnimationData>> dataToCreateLease =
                Buffers<AnimationData>.List.Get(out List<AnimationData> dataToCreate);
            if (searchTerms.Length == 0)
            {
                dataToCreate.AddRange(animationData);
            }
            else
            {
                foreach (AnimationData data in animationData)
                {
                    string lowerName = (data.animationName ?? string.Empty).ToLowerInvariant();
                    bool allMatch = true;
                    for (int i = 0; i < searchTerms.Length; i++)
                    {
                        if (lowerName.IndexOf(searchTerms[i], StringComparison.Ordinal) < 0)
                        {
                            allMatch = false;
                            break;
                        }
                    }
                    if (allMatch)
                    {
                        dataToCreate.Add(data);
                    }
                }
                this.Log(
                    $"Creating animations based on current search filter '{_searchString}'. Only {dataToCreate.Count} out of {animationData.Count} items will be processed."
                );
            }

            if (dataToCreate.Count == 0)
            {
                this.LogError(
                    $"No animation data matches the current search filter '{_searchString}'. Nothing to create."
                );
                return;
            }

            int totalAnimations = dataToCreate.Count;
            int currentAnimationIndex = 0;
            bool errorOccurred = false;

            try
            {
                using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
                {
                    foreach (AnimationData data in dataToCreate)
                    {
                        currentAnimationIndex++;
                        string animationName = data.animationName;
                        if (string.IsNullOrWhiteSpace(animationName))
                        {
                            this.LogWarn(
                                $"Ignoring animation data entry (original index unknown due to filtering) without an animation name."
                            );
                            continue;
                        }

                        Utils.EditorUi.ShowProgress(
                            "Creating Animations",
                            $"Processing '{animationName}' ({currentAnimationIndex}/{totalAnimations})",
                            (float)currentAnimationIndex / totalAnimations
                        );

                        List<Sprite> frames = data.frames;
                        if (frames is not { Count: > 0 })
                        {
                            this.LogWarn(
                                $"Ignoring animation '{animationName}' because it has no frames."
                            );
                            continue;
                        }

                        using PooledResource<List<Sprite>> validFramesResource =
                            Buffers<Sprite>.List.Get(out List<Sprite> validFrames);
                        foreach (Sprite f in frames)
                        {
                            if (f != null)
                            {
                                validFrames.Add(f);
                            }
                        }
                        if (validFrames.Count == 0)
                        {
                            this.LogWarn(
                                $"Ignoring animation '{animationName}' because it only contains null frames."
                            );
                            continue;
                        }

                        validFrames.Sort(
                            (s1, s2) => EditorUtility.NaturalCompare(s1.name, s2.name)
                        );

                        AnimationClip animationClip = CreateAnimationClip(data, validFrames);

                        string firstFramePath = AssetDatabase.GetAssetPath(validFrames[0]);
                        string assetPath =
                            Path.GetDirectoryName(firstFramePath).SanitizePath() ?? "Assets";
                        if (!assetPath.EndsWith("/"))
                        {
                            assetPath += "/";
                        }

                        string finalPath = AssetDatabase.GenerateUniqueAssetPath(
                            $"{assetPath}{animationName}.anim"
                        );
                        AssetDatabase.CreateAsset(animationClip, finalPath);
                        this.Log($"Created animation at '{finalPath}'.");
                    }
                }
            }
            catch (Exception e)
            {
                errorOccurred = true;
                this.LogError($"An error occurred during animation creation", e);
            }
            finally
            {
                Utils.EditorUi.ClearProgress();
                if (!errorOccurred)
                {
                    this.Log($"Finished creating {totalAnimations} animations.");
                }
                else
                {
                    this.LogError($"Animation creation finished with errors. Check console.");
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        private AnimationClip CreateAnimationClip(AnimationData data, List<Sprite> validFrames)
        {
            float baseFrameRate =
                data.framesPerSecond > 0
                    ? data.framesPerSecond
                    : AnimationData.DefaultFramesPerSecond;

            AnimationClip clip = new() { frameRate = baseFrameRate };

            // Use exact-size array for Unity API. SystemArrayPool returns arrays rounded up to
            // the next power-of-2, and AnimationUtility.SetObjectReferenceCurve uses the array's
            // Length property, causing broken animations from null trailing elements.
            ObjectReferenceKeyframe[] keyframes = new ObjectReferenceKeyframe[validFrames.Count];

            float currentTime = 0f;

            for (int i = 0; i < validFrames.Count; i++)
            {
                keyframes[i].time = currentTime;
                keyframes[i].value = validFrames[i];

                if (i < validFrames.Count - 1)
                {
                    float fps;
                    if (data.framerateMode == FramerateMode.Curve)
                    {
                        float normalizedPosition =
                            validFrames.Count > 1 ? (float)i / (validFrames.Count - 1) : 0f;

                        fps = data.framesPerSecondCurve.Evaluate(normalizedPosition);
                        if (fps <= 0)
                        {
                            fps = baseFrameRate;
                        }
                    }
                    else
                    {
                        fps = baseFrameRate;
                    }

                    currentTime += 1f / fps;
                }
            }

            AnimationUtility.SetObjectReferenceCurve(
                clip,
                EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"),
                keyframes
            );

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = data.loop;
            settings.cycleOffset = Mathf.Clamp01(data.cycleOffset);
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            return clip;
        }

        private void UpdateRegex()
        {
            if (_compiledRegex == null || _lastUsedRegex != spriteNameRegex)
            {
                try
                {
                    _compiledRegex = new Regex(
                        spriteNameRegex,
                        RegexOptions.Compiled | RegexOptions.CultureInvariant
                    );
                    _lastUsedRegex = spriteNameRegex;
                    _errorMessage = "";
                    this.Log($"Regex updated to: {spriteNameRegex}");
                }
                catch (ArgumentException e)
                {
                    _compiledRegex = null;
                    _lastUsedRegex = spriteNameRegex;
                    _errorMessage = $"Invalid Regex: {e.Message}";
                    this.LogError($"Invalid Regex '{spriteNameRegex}'", e);
                }
            }
        }

        private void UpdateGroupRegex()
        {
            _groupRegexErrorMessage = string.Empty;
            _compiledGroupRegex = null;
            if (!useCustomGroupRegex)
            {
                _lastGroupRegex = customGroupRegex;
                return;
            }

            if (string.IsNullOrWhiteSpace(customGroupRegex))
            {
                _lastGroupRegex = customGroupRegex;
                _groupRegexErrorMessage = "Custom Group Regex enabled but pattern is empty.";
                return;
            }

            try
            {
                RegexOptions options = RegexOptions.Compiled | RegexOptions.CultureInvariant;
                if (customGroupRegexIgnoreCase)
                {
                    options |= RegexOptions.IgnoreCase;
                }
                _compiledGroupRegex = new Regex(customGroupRegex, options);
                _lastGroupRegex = customGroupRegex;
            }
            catch (ArgumentException e)
            {
                _compiledGroupRegex = null;
                _lastGroupRegex = customGroupRegex;
                _groupRegexErrorMessage = $"Invalid Custom Group Regex: {e.Message}";
                this.LogError($"Invalid Custom Group Regex '{customGroupRegex}'", e);
            }
        }

        private void FindAndFilterSprites()
        {
            _filteredSprites.Clear();
            _matchedSpriteCount = 0;
            _unmatchedSpriteCount = 0;

            if (animationSources is not { Count: > 0 } || _compiledRegex == null)
            {
                if (_compiledRegex == null && !string.IsNullOrWhiteSpace(spriteNameRegex))
                {
                    this.LogWarn(
                        $"Cannot find sprites, regex pattern '{spriteNameRegex}' is invalid."
                    );
                }
                else if (animationSources is not { Count: > 0 })
                {
                    this.LogWarn($"Cannot find sprites, no animation sources specified.");
                }
                return;
            }

            using PooledResource<List<string>> searchPathsLease = Buffers<string>.List.Get(
                out List<string> searchPaths
            );
            for (int i = 0; i < animationSources.Count; i++)
            {
                Object source = animationSources[i];
                if (source == null)
                {
                    continue;
                }
                string path = AssetDatabase.GetAssetPath(source);
                if (!string.IsNullOrWhiteSpace(path) && AssetDatabase.IsValidFolder(path))
                {
                    searchPaths.Add(path);
                }
                else if (source != null)
                {
                    this.LogWarn($"Source '{source.name}' is not a valid folder. Skipping.");
                }
            }

            if (searchPaths.Count == 0)
            {
                this.LogWarn($"No valid folders found in Animation Sources.");
                return;
            }

            string[] assetGuids = AssetDatabase.FindAssets("t:sprite", searchPaths.ToArray());
            int totalAssets = assetGuids.Length;
            this.Log($"Found {totalAssets} total sprite assets in specified paths.");

            try
            {
                Utils.EditorUi.ShowProgress(
                    "Finding and Filtering Sprites",
                    $"Scanning {assetGuids.Length} assets...",
                    0f
                );

                for (int i = 0; i < totalAssets; i++)
                {
                    string guid = assetGuids[i];
                    string path = AssetDatabase.GUIDToAssetPath(guid);

                    if (i % 20 == 0 || i == totalAssets - 1)
                    {
                        float progress = (i + 1) / (float)totalAssets;
                        Utils.EditorUi.ShowProgress(
                            "Finding and Filtering Sprites",
                            $"Checking: {Path.GetFileName(path)} ({i + 1}/{assetGuids.Length})",
                            progress
                        );
                    }

                    Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);

                    if (sprite != null)
                    {
                        if (_compiledRegex.IsMatch(sprite.name))
                        {
                            _filteredSprites.Add(sprite);
                            _matchedSpriteCount++;
                        }
                        else
                        {
                            _unmatchedSpriteCount++;
                        }
                    }
                }
                this.Log(
                    $"Sprite filtering complete. Matched: {_matchedSpriteCount}, Unmatched: {_unmatchedSpriteCount}."
                );
            }
            finally
            {
                Utils.EditorUi.ClearProgress();
            }
        }

        private void AutoParseSprites()
        {
            if (_filteredSprites.Count == 0)
            {
                this.LogWarn($"Cannot Auto-Parse, no matched sprites available.");
                return;
            }

            try
            {
                Dictionary<string, Dictionary<string, List<(int index, Sprite sprite)>>> groups =
                    GroupFilteredSprites(withProgress: true);

                if (groups.Count == 0)
                {
                    this.LogWarn(
                        $"Auto-parsing did not result in any animation groups. Check naming."
                    );
                    return;
                }

                int removedCount = animationData.RemoveAll(data => data.isCreatedFromAutoParse);
                this.Log($"Removed {removedCount} previously auto-parsed animation entries.");

                int added = ApplyAutoParseGroups(groups);
                this.Log($"Auto-parsed into {added} new animation groups.");
            }
            finally
            {
                Utils.EditorUi.ClearProgress();
                _serializedObject.Update();
            }
        }

        private static string SanitizeName(string inputName)
        {
            inputName = inputName.Replace(" ", "_");
            inputName = Regex.Replace(inputName, @"[^a-zA-Z0-9_]", "");

            if (string.IsNullOrWhiteSpace(inputName))
            {
                return "Default_Animation";
            }

            return inputName.Trim('_');
        }

        private static string StripDensitySuffix(string name)
        {
            return Regex.Replace(name, @"@\d+(?:\.\d+)?x$", string.Empty);
        }

        private static bool TryExtractBaseAndIndex(string name, out string baseName, out int index)
        {
            baseName = null;
            index = -1;

            Match m = s_ParenIndexRegex.Match(name);
            if (m.Success)
            {
                baseName = m.Groups["base"].Value;
                _ = int.TryParse(m.Groups["index"].Value, out index);
                baseName = baseName.TrimEnd('_', '-', '.', ' ');
                return true;
            }

            m = s_SeparatorIndexRegex.Match(name);
            if (m.Success)
            {
                baseName = m.Groups["base"].Value;
                _ = int.TryParse(m.Groups["index"].Value, out index);
                baseName = baseName.TrimEnd('_', '-', '.', ' ');
                return true;
            }

            m = s_TrailingIndexRegex.Match(name);
            if (m.Success && m.Groups["base"].Length > 0)
            {
                baseName = m.Groups["base"].Value;
                _ = int.TryParse(m.Groups["index"].Value, out index);
                baseName = baseName.TrimEnd('_', '-', '.', ' ');
                return true;
            }

            return false;
        }

        private int ComputeSourcesHash()
        {
            if (animationSources == null || animationSources.Count == 0)
            {
                return 0;
            }

            return Objects.EnumerableHashCode(EnumerateSourceHashes());

            IEnumerable<(int id, string path)> EnumerateSourceHashes()
            {
                for (int i = 0; i < animationSources.Count; i++)
                {
                    Object src = animationSources[i];
                    int id = src != null ? src.GetUnityObjectId() : 0;
                    string path = src != null ? AssetDatabase.GetAssetPath(src) : string.Empty;
                    yield return (id, path);
                }
            }
        }

        private Dictionary<
            string,
            Dictionary<string, List<(int index, Sprite sprite)>>
        > GroupFilteredSprites(bool withProgress)
        {
            Dictionary<
                string,
                Dictionary<string, List<(int index, Sprite sprite)>>
            > spritesByBaseAndAssetPath = new(StringComparer.Ordinal);

            int total = _filteredSprites.Count;
            int processed = 0;

            foreach (Sprite sprite in _filteredSprites)
            {
                processed++;
                if (withProgress && (processed % 10 == 0 || processed == total))
                {
                    Utils.EditorUi.ShowProgress(
                        "Auto-Parsing Sprites",
                        $"Processing: {sprite.name} ({processed}/{total})",
                        (float)processed / total
                    );
                }

                string assetPath = AssetDatabase.GetAssetPath(sprite);
                string directoryPath =
                    Path.GetDirectoryName(assetPath).SanitizePath() ?? string.Empty;
                string frameName = StripDensitySuffix(sprite.name);

                string baseName;
                int frameIndex;

                if (useCustomGroupRegex && _compiledGroupRegex != null)
                {
                    Match m = _compiledGroupRegex.Match(frameName);
                    if (m.Success)
                    {
                        Group baseGroup = m.Groups["base"];
                        Group indexGroup = m.Groups["index"];
                        baseName = baseGroup.Success ? baseGroup.Value : frameName;
                        frameIndex =
                            indexGroup.Success && int.TryParse(indexGroup.Value, out int idx)
                                ? idx
                                : -1;
                    }
                    else if (!TryExtractBaseAndIndex(frameName, out baseName, out frameIndex))
                    {
                        baseName = frameName;
                        frameIndex = -1;
                    }
                }
                else if (!TryExtractBaseAndIndex(frameName, out baseName, out frameIndex))
                {
                    baseName = frameName;
                    frameIndex = -1;
                }

                if (string.IsNullOrWhiteSpace(baseName))
                {
                    this.LogWarn(
                        $"Could not extract valid base name for '{frameName}' at '{assetPath}'. Skipping."
                    );
                    continue;
                }

                if (
                    !spritesByBaseAndAssetPath.TryGetValue(
                        directoryPath,
                        out Dictionary<string, List<(int index, Sprite sprite)>> byBase
                    )
                )
                {
                    byBase = new Dictionary<string, List<(int index, Sprite sprite)>>(
                        groupingCaseInsensitive
                            ? StringComparer.OrdinalIgnoreCase
                            : StringComparer.Ordinal
                    );
                    spritesByBaseAndAssetPath.Add(directoryPath, byBase);
                }

                List<(int index, Sprite sprite)> list = byBase.GetOrAdd(baseName);
                list.Add((frameIndex, sprite));
            }

            return spritesByBaseAndAssetPath;
        }

        private int ApplyAutoParseGroups(
            Dictionary<string, Dictionary<string, List<(int index, Sprite sprite)>>> groups
        )
        {
            int addedCount = 0;

            using PooledResource<HashSet<string>> usedNamesLease = SetBuffers<string>
                .GetHashSetPool(StringComparer.OrdinalIgnoreCase)
                .Get(out HashSet<string> usedNames);
            foreach (AnimationData data in animationData)
            {
                if (!data.isCreatedFromAutoParse && !string.IsNullOrWhiteSpace(data.animationName))
                {
                    usedNames.Add(data.animationName);
                }
            }

            foreach (
                KeyValuePair<
                    string,
                    Dictionary<string, List<(int index, Sprite sprite)>>
                > kvpAssetPath in groups
            )
            {
                string folderName = new DirectoryInfo(kvpAssetPath.Key).Name;
                foreach (
                    (string baseKey, List<(int index, Sprite sprite)> entries) in kvpAssetPath.Value
                )
                {
                    if (entries.Count == 0)
                    {
                        continue;
                    }

                    bool hasAnyIndex = entries.Exists(e => e.index >= 0);
                    if (strictNumericOrdering)
                    {
                        if (hasAnyIndex)
                        {
                            entries.Sort((a, b) => a.index.CompareTo(b.index));
                        }
                    }
                    else
                    {
                        if (hasAnyIndex)
                        {
                            entries.Sort((a, b) => a.index.CompareTo(b.index));
                        }
                        else
                        {
                            entries.Sort(
                                (a, b) => EditorUtility.NaturalCompare(a.sprite.name, b.sprite.name)
                            );
                        }
                    }

                    List<Sprite> framesForAnim = new(entries.Count);
                    foreach ((int index, Sprite sprite) e in entries)
                    {
                        framesForAnim.Add(e.sprite);
                    }

                    string finalAnimName = ComposeFinalName(
                        baseKey,
                        kvpAssetPath.Key,
                        usedNames,
                        out bool _
                    );
                    usedNames.Add(finalAnimName);

                    animationData.Add(
                        new AnimationData
                        {
                            frames = framesForAnim,
                            framesPerSecond = AnimationData.DefaultFramesPerSecond,
                            animationName = finalAnimName,
                            isCreatedFromAutoParse = true,
                            loop = false,
                            framerateMode = FramerateMode.Constant,
                            framesPerSecondCurve = AnimationCurve.Constant(
                                0f,
                                1f,
                                AnimationData.DefaultFramesPerSecond
                            ),
                            cycleOffset = 0f,
                        }
                    );
                    addedCount++;
                }
            }

            return addedCount;
        }

        private static string EnsureUniqueName(string baseName, ISet<string> used)
        {
            if (!used.Contains(baseName))
            {
                return baseName;
            }
            int counter = 2;
            string candidate;
            do
            {
                candidate = $"{baseName}_{counter}";
                counter++;
            } while (used.Contains(candidate));
            return candidate;
        }

        private string ComposeFinalName(
            string baseKey,
            string directoryPath,
            ISet<string> usedNames,
            out bool duplicateResolved
        )
        {
            string finalNameCore = baseKey;
            string folderPrefix = GetFolderPrefix(directoryPath);
            if (!string.IsNullOrEmpty(folderPrefix))
            {
                finalNameCore = folderPrefix + "_" + finalNameCore;
            }
            if (!string.IsNullOrEmpty(autoParseNamePrefix))
            {
                finalNameCore = autoParseNamePrefix + finalNameCore;
            }
            if (!string.IsNullOrEmpty(autoParseNameSuffix))
            {
                finalNameCore = finalNameCore + autoParseNameSuffix;
            }
            string finalAnimName = SanitizeName(finalNameCore);

            duplicateResolved = false;
            if (resolveDuplicateAnimationNames && usedNames != null)
            {
                if (usedNames.Contains(finalAnimName))
                {
                    finalAnimName = EnsureUniqueName(finalAnimName, usedNames);
                    duplicateResolved = true;
                }
            }
            return finalAnimName;
        }

        private string GetFolderPrefix(string directoryPath)
        {
            if (!includeFolderNameInAnimName && !includeFullFolderPathInAnimName)
            {
                return string.Empty;
            }
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return string.Empty;
            }
            string sanitized = directoryPath.SanitizePath();
            if (includeFullFolderPathInAnimName)
            {
                if (sanitized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                {
                    sanitized = sanitized.Substring("Assets/".Length);
                }
                sanitized = sanitized.Trim('/');
                sanitized = sanitized.Replace('/', '_');
                return SanitizeName(sanitized);
            }
            return new DirectoryInfo(directoryPath).Name;
        }

        private void GenerateAutoParseDryRun()
        {
            _autoParseDryRun.Clear();
            Dictionary<string, Dictionary<string, List<(int index, Sprite sprite)>>> groups =
                GroupFilteredSprites(withProgress: false);

            using PooledResource<HashSet<string>> usedNamesLease = SetBuffers<string>
                .GetHashSetPool(StringComparer.OrdinalIgnoreCase)
                .Get(out HashSet<string> usedNames);
            foreach (AnimationData data in animationData)
            {
                if (!data.isCreatedFromAutoParse && !string.IsNullOrWhiteSpace(data.animationName))
                {
                    usedNames.Add(data.animationName);
                }
            }

            foreach (
                KeyValuePair<
                    string,
                    Dictionary<string, List<(int index, Sprite sprite)>>
                > kvp in groups
            )
            {
                string dir = kvp.Key;
                foreach ((string baseKey, List<(int index, Sprite sprite)> entries) in kvp.Value)
                {
                    bool hasAnyIndex = entries.Exists(e => e.index >= 0);
                    if (strictNumericOrdering)
                    {
                        if (hasAnyIndex)
                        {
                            entries.Sort((a, b) => a.index.CompareTo(b.index));
                        }
                    }
                    else
                    {
                        if (hasAnyIndex)
                        {
                            entries.Sort((a, b) => a.index.CompareTo(b.index));
                        }
                        else
                        {
                            entries.Sort(
                                (a, b) => EditorUtility.NaturalCompare(a.sprite.name, b.sprite.name)
                            );
                        }
                    }

                    string finalName = ComposeFinalName(
                        baseKey,
                        dir,
                        usedNames,
                        out bool wasResolved
                    );
                    usedNames.Add(finalName);

                    string folderPath = dir;
                    if (!folderPath.EndsWith("/"))
                    {
                        folderPath += "/";
                    }
                    string finalAssetPath = folderPath + finalName + ".anim";

                    _autoParseDryRun.Add(
                        new AutoParseDryRunRecord
                        {
                            folderPath = dir,
                            finalName = finalName,
                            finalAssetPath = finalAssetPath,
                            count = entries.Count,
                            hasIndex = hasAnyIndex,
                            duplicateResolved = wasResolved,
                        }
                    );
                }
            }
        }

        private void GenerateAutoParsePreview()
        {
            _autoParsePreview.Clear();
            Dictionary<string, Dictionary<string, List<(int index, Sprite sprite)>>> groups =
                GroupFilteredSprites(withProgress: false);
            foreach (
                KeyValuePair<
                    string,
                    Dictionary<string, List<(int index, Sprite sprite)>>
                > dir in groups
            )
            {
                string folderName = new DirectoryInfo(dir.Key).Name;
                foreach ((string baseKey, List<(int index, Sprite sprite)> entries) in dir.Value)
                {
                    AutoParsePreviewRecord rec = new()
                    {
                        folder = folderName,
                        baseName = baseKey,
                        count = entries.Count,
                        hasIndex = entries.Exists(e => e.index >= 0),
                    };
                    _autoParsePreview.Add(rec);
                }
            }
            _autoParsePreview.Sort(
                (a, b) => string.Compare(a.folder, b.folder, StringComparison.Ordinal)
            );
        }

        internal static float GetCurrentFpsForTests(AnimationData data, int frameIndex)
        {
            if (data.framerateMode == FramerateMode.Constant)
            {
                return data.framesPerSecond > 0
                    ? data.framesPerSecond
                    : AnimationData.DefaultFramesPerSecond;
            }

            float normalizedPosition =
                data.frames.Count > 1 ? (float)frameIndex / (data.frames.Count - 1) : 0f;

            float fps = data.framesPerSecondCurve.Evaluate(normalizedPosition);
            return fps > 0 ? fps : AnimationData.DefaultFramesPerSecond;
        }

        internal static AnimationClip CreateAnimationClipForTests(
            AnimationData data,
            List<Sprite> validFrames
        )
        {
            float baseFrameRate =
                data.framesPerSecond > 0
                    ? data.framesPerSecond
                    : AnimationData.DefaultFramesPerSecond;

            AnimationClip clip = new() { frameRate = baseFrameRate };

            ObjectReferenceKeyframe[] keyframes = new ObjectReferenceKeyframe[validFrames.Count];

            float currentTime = 0f;

            for (int i = 0; i < validFrames.Count; i++)
            {
                keyframes[i].time = currentTime;
                keyframes[i].value = validFrames[i];

                if (i < validFrames.Count - 1)
                {
                    float fps;
                    if (data.framerateMode == FramerateMode.Curve)
                    {
                        float normalizedPosition =
                            validFrames.Count > 1 ? (float)i / (validFrames.Count - 1) : 0f;

                        fps = data.framesPerSecondCurve.Evaluate(normalizedPosition);
                        if (fps <= 0)
                        {
                            fps = baseFrameRate;
                        }
                    }
                    else
                    {
                        fps = baseFrameRate;
                    }

                    currentTime += 1f / fps;
                }
            }

            AnimationUtility.SetObjectReferenceCurve(
                clip,
                EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"),
                keyframes
            );

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = data.loop;
            settings.cycleOffset = Mathf.Clamp01(data.cycleOffset);
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            return clip;
        }

        internal static int CalculateScrubberFrame(float scrubberValue, int frameCount)
        {
            if (frameCount <= 0)
            {
                return 0;
            }
            float clampedValue = Mathf.Clamp01(scrubberValue);
            // Use FloorToInt with +0.5f to ensure "round half up" behavior
            // Mathf.RoundToInt uses banker's rounding (rounds 0.5 to nearest even),
            // which is counterintuitive for UI scrubbers where users expect 0.5 -> 1
            int frame = Mathf.FloorToInt(clampedValue * (frameCount - 1) + 0.5f);
            return Mathf.Clamp(frame, 0, frameCount - 1);
        }

        internal static float CalculateCycleOffsetClamped(float inputOffset)
        {
            return Mathf.Clamp01(inputOffset);
        }

        /// <summary>
        /// Creates an AnimationCreatorConfig from the current window state.
        /// </summary>
        /// <returns>A config object representing the current settings.</returns>
        internal AnimationCreatorConfig CreateConfigFromCurrentState()
        {
            AnimationCreatorConfig config = new()
            {
                version = AnimationCreatorConfig.CurrentVersion,
                spriteNameRegex = spriteNameRegex,
                autoRefresh = autoRefresh,
                groupingCaseInsensitive = groupingCaseInsensitive,
                includeFolderNameInAnimName = includeFolderNameInAnimName,
                includeFullFolderPathInAnimName = includeFullFolderPathInAnimName,
                autoParseNamePrefix = autoParseNamePrefix,
                autoParseNameSuffix = autoParseNameSuffix,
                useCustomGroupRegex = useCustomGroupRegex,
                customGroupRegex = customGroupRegex,
                customGroupRegexIgnoreCase = customGroupRegexIgnoreCase,
                resolveDuplicateAnimationNames = resolveDuplicateAnimationNames,
                strictNumericOrdering = strictNumericOrdering,
                animationEntries = new List<AnimationCreatorConfig.AnimationDataEntry>(),
            };

            foreach (AnimationData data in animationData)
            {
                AnimationCreatorConfig.AnimationDataEntry entry = new()
                {
                    animationName = data.animationName,
                    framesPerSecond = data.framesPerSecond,
                    isCreatedFromAutoParse = data.isCreatedFromAutoParse,
                    loop = data.loop,
                    framerateMode = data.framerateMode,
                    cycleOffset = data.cycleOffset,
                    framePaths = new List<string>(),
                    curveKeyframes = AnimationCreatorConfig.SerializeCurve(
                        data.framesPerSecondCurve
                    ),
                    curvePreWrapMode = data.framesPerSecondCurve?.preWrapMode ?? WrapMode.Clamp,
                    curvePostWrapMode = data.framesPerSecondCurve?.postWrapMode ?? WrapMode.Clamp,
                };

                foreach (Sprite sprite in data.frames)
                {
                    if (sprite != null)
                    {
                        string path = AssetDatabase.GetAssetPath(sprite);
                        if (!string.IsNullOrEmpty(path))
                        {
                            entry.framePaths.Add(path);
                        }
                    }
                }

                config.animationEntries.Add(entry);
            }

            return config;
        }

        /// <summary>
        /// Applies an AnimationCreatorConfig to the current window state.
        /// </summary>
        /// <param name="config">The config to apply.</param>
        internal void ApplyConfigToCurrentState(AnimationCreatorConfig config)
        {
            if (config == null)
            {
                return;
            }

            AnimationCreatorConfig.MigrateConfig(config);

            spriteNameRegex = config.spriteNameRegex;
            autoRefresh = config.autoRefresh;
            groupingCaseInsensitive = config.groupingCaseInsensitive;
            includeFolderNameInAnimName = config.includeFolderNameInAnimName;
            includeFullFolderPathInAnimName = config.includeFullFolderPathInAnimName;
            autoParseNamePrefix = config.autoParseNamePrefix;
            autoParseNameSuffix = config.autoParseNameSuffix;
            useCustomGroupRegex = config.useCustomGroupRegex;
            customGroupRegex = config.customGroupRegex;
            customGroupRegexIgnoreCase = config.customGroupRegexIgnoreCase;
            resolveDuplicateAnimationNames = config.resolveDuplicateAnimationNames;
            strictNumericOrdering = config.strictNumericOrdering;

            animationData.Clear();
            foreach (AnimationCreatorConfig.AnimationDataEntry entry in config.animationEntries)
            {
                AnimationData data = new()
                {
                    animationName = entry.animationName,
                    framesPerSecond = entry.framesPerSecond,
                    isCreatedFromAutoParse = entry.isCreatedFromAutoParse,
                    loop = entry.loop,
                    framerateMode = entry.framerateMode,
                    cycleOffset = entry.cycleOffset,
                    framesPerSecondCurve = AnimationCreatorConfig.DeserializeCurve(
                        entry.curveKeyframes,
                        entry.curvePreWrapMode,
                        entry.curvePostWrapMode
                    ),
                    frames = new List<Sprite>(),
                };

                foreach (string path in entry.framePaths)
                {
                    if (string.IsNullOrEmpty(path))
                    {
                        continue;
                    }

                    Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    if (sprite != null)
                    {
                        data.frames.Add(sprite);
                    }
                    else
                    {
                        this.LogWarn(
                            $"Could not load sprite at path '{path}' for animation '{entry.animationName}'."
                        );
                    }
                }

                animationData.Add(data);
            }

            UpdateRegex();
            UpdateGroupRegex();
            FindAndFilterSprites();
            _serializedObject.Update();
            Repaint();
        }

        /// <summary>
        /// Saves the current configuration to a JSON file in the specified folder.
        /// </summary>
        /// <param name="folderPath">The folder path to save the config to.</param>
        /// <returns>True if the config was saved successfully, false otherwise.</returns>
        internal bool SaveConfig(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
            {
                return false;
            }

            try
            {
                string configPath = AnimationCreatorConfig.GetConfigPath(folderPath);
                string fullConfigPath = Path.GetFullPath(configPath);

                AnimationCreatorConfig config = CreateConfigFromCurrentState();
                string json = Serializer.JsonStringify(config, pretty: true);
                File.WriteAllText(fullConfigPath, json, Encoding.UTF8);

                _loadedConfigs[folderPath] = config;
                this.Log($"Saved animation creator config to '{configPath}'.");

                AssetDatabase.Refresh();
                return true;
            }
            catch (Exception e)
            {
                this.LogError($"Failed to save config to '{folderPath}'", e);
                return false;
            }
        }

        /// <summary>
        /// Saves configs to all animation source folders.
        /// </summary>
        /// <returns>The number of configs successfully saved.</returns>
        internal int SaveAllConfigs()
        {
            int savedCount = 0;
            foreach (Object source in animationSources)
            {
                if (source == null)
                {
                    continue;
                }

                string path = AssetDatabase.GetAssetPath(source);
                if (!string.IsNullOrWhiteSpace(path) && AssetDatabase.IsValidFolder(path))
                {
                    if (SaveConfig(path))
                    {
                        savedCount++;
                    }
                }
            }

            this.Log($"Saved {savedCount} animation creator configs.");
            return savedCount;
        }

        /// <summary>
        /// Loads the configuration from a JSON file in the specified folder.
        /// </summary>
        /// <param name="folderPath">The folder path to load the config from.</param>
        /// <returns>True if the config was loaded successfully, false otherwise.</returns>
        internal bool LoadConfig(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
            {
                return false;
            }

            try
            {
                string configPath = AnimationCreatorConfig.GetConfigPath(folderPath);
                string fullConfigPath = Path.GetFullPath(configPath);

                if (!File.Exists(fullConfigPath))
                {
                    return false;
                }

                string json = File.ReadAllText(fullConfigPath, Encoding.UTF8);
                AnimationCreatorConfig config = Serializer.JsonDeserialize<AnimationCreatorConfig>(
                    json
                );

                if (config == null)
                {
                    return false;
                }

                _loadedConfigs[folderPath] = config;
                ApplyConfigToCurrentState(config);
                this.Log($"Loaded animation creator config from '{configPath}'.");
                return true;
            }
            catch (Exception e)
            {
                this.LogError($"Failed to load config from '{folderPath}'", e);
                return false;
            }
        }

        /// <summary>
        /// Attempts to auto-load configs from all animation source folders.
        /// Loads the first found config.
        /// </summary>
        /// <returns>True if any config was loaded, false otherwise.</returns>
        internal bool TryAutoLoadConfigs()
        {
            foreach (Object source in animationSources)
            {
                if (source == null)
                {
                    continue;
                }

                string path = AssetDatabase.GetAssetPath(source);
                if (!string.IsNullOrWhiteSpace(path) && AssetDatabase.IsValidFolder(path))
                {
                    string configPath = AnimationCreatorConfig.GetConfigPath(path);
                    string fullConfigPath = Path.GetFullPath(configPath);

                    if (File.Exists(fullConfigPath))
                    {
                        if (LoadConfig(path))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if any animation source folder has a saved config.
        /// </summary>
        /// <returns>True if any config file exists, false otherwise.</returns>
        internal bool HasAnyConfig()
        {
            foreach (Object source in animationSources)
            {
                if (source == null)
                {
                    continue;
                }

                string path = AssetDatabase.GetAssetPath(source);
                if (!string.IsNullOrWhiteSpace(path) && AssetDatabase.IsValidFolder(path))
                {
                    string configPath = AnimationCreatorConfig.GetConfigPath(path);
                    string fullConfigPath = Path.GetFullPath(configPath);

                    if (File.Exists(fullConfigPath))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Gets all folder paths that have saved configs.
        /// </summary>
        /// <returns>List of folder paths with configs.</returns>
        internal List<string> GetFoldersWithConfigs()
        {
            List<string> folders = new();
            foreach (Object source in animationSources)
            {
                if (source == null)
                {
                    continue;
                }

                string path = AssetDatabase.GetAssetPath(source);
                if (!string.IsNullOrWhiteSpace(path) && AssetDatabase.IsValidFolder(path))
                {
                    string configPath = AnimationCreatorConfig.GetConfigPath(path);
                    string fullConfigPath = Path.GetFullPath(configPath);

                    if (File.Exists(fullConfigPath))
                    {
                        folders.Add(path);
                    }
                }
            }

            return folders;
        }

        /// <summary>
        /// Resets the window to default settings, discarding loaded config.
        /// </summary>
        /// <param name="folderPath">Optional folder path whose config to delete.</param>
        internal void ResetToDefault(string folderPath = null)
        {
            spriteNameRegex = ".*";
            autoRefresh = true;
            groupingCaseInsensitive = true;
            includeFolderNameInAnimName = false;
            includeFullFolderPathInAnimName = false;
            autoParseNamePrefix = string.Empty;
            autoParseNameSuffix = string.Empty;
            useCustomGroupRegex = false;
            customGroupRegex = string.Empty;
            customGroupRegexIgnoreCase = true;
            resolveDuplicateAnimationNames = true;
            strictNumericOrdering = false;
            animationData.Clear();

            if (!string.IsNullOrEmpty(folderPath))
            {
                _loadedConfigs.Remove(folderPath);
            }
            else
            {
                _loadedConfigs.Clear();
            }

            UpdateRegex();
            UpdateGroupRegex();
            FindAndFilterSprites();
            _serializedObject.Update();
            Repaint();

            this.Log($"Reset animation creator settings to defaults.");
        }

        /// <summary>
        /// Deletes a saved config file.
        /// </summary>
        /// <param name="folderPath">The folder path whose config to delete.</param>
        /// <returns>True if the config was deleted, false otherwise.</returns>
        internal bool DeleteConfig(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
            {
                return false;
            }

            try
            {
                string configPath = AnimationCreatorConfig.GetConfigPath(folderPath);
                string fullConfigPath = Path.GetFullPath(configPath);

                if (!File.Exists(fullConfigPath))
                {
                    return false;
                }

                File.Delete(fullConfigPath);
                _loadedConfigs.Remove(folderPath);
                this.Log($"Deleted animation creator config at '{configPath}'.");

                AssetDatabase.Refresh();
                return true;
            }
            catch (Exception e)
            {
                this.LogError($"Failed to delete config at '{folderPath}'", e);
                return false;
            }
        }
    }

#endif
}
