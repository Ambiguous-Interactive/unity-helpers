// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using CustomEditors;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Utils;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using Object = UnityEngine.Object;

    public sealed class FitTextureSizeWindow : EditorWindow
    {
        private static bool SuppressUserPrompts { get; set; }
        internal SerializedObject SerializedStateForTesting => _serializedObject;

        internal FitMode _fitMode = FitMode.GrowAndShrink;

        [SerializeField]
        internal List<Object> _textureSourcePaths = new();

        // Label-query GUIDs avoid reloading per-asset labels for case-insensitive filtering.
        internal bool _hasLastRunSummary;
        internal int _lastRunTotal;
        internal int _lastRunChanged;
        internal int _lastRunGrows;
        internal int _lastRunShrinks;
        internal int _lastRunUnchanged;

        [SerializeField]
        internal bool _useSelectionOnly;

        [SerializeField]
        internal bool _onlySprites;

        [SerializeField]
        internal int _minAllowedTextureSize = 32;

        [SerializeField]
        internal int _maxAllowedTextureSize = 8192;

        [SerializeField]
        internal bool _applyToStandalone;

        [SerializeField]
        internal bool _applyToAndroid;

        [SerializeField]
        internal bool _applyToiOS;

        [SerializeField]
        internal string _nameFilter = string.Empty;

        [SerializeField]
        internal bool _useRegexForName;

        [SerializeField]
        internal bool _caseSensitiveNameFilter;

        [SerializeField]
        internal string _labelFilterCsv = string.Empty;
        private Vector2 _scrollPosition = Vector2.zero;
        private SerializedObject _serializedObject;
        private SerializedProperty _textureSourcePathsProperty;
        private int _potentialChangeCount = -1;
        private int _potentialGrowCount;
        private int _potentialShrinkCount;
        private int _potentialUnchangedCount;

        static FitTextureSizeWindow()
        {
            /* Application.isBatchMode throws from a ScriptableObject static initializer in
            the editor Test Runner; the catch keeps the type initializable there. */
            try
            {
                if (Application.isBatchMode || EditorUtilities.IsInvokedByTestRunner())
                {
                    SuppressUserPrompts = true;
                }
            }
            catch { }
        }

        [MenuItem("Tools/Wallstop Studios/Unity Helpers/Fit Texture Size", priority = -1)]
        public static void ShowWindow()
        {
            GetWindow<FitTextureSizeWindow>("Fit Texture Size");
        }

        /// <summary>
        /// Computes the target <c>maxTextureSize</c> for a source texture given its dimensions,
        /// its current max size, the fit mode, and the allowed min/max bounds. Pure: depends only
        /// on integers, performs NO AssetDatabase/importer I/O, so it is exercised by fast unit
        /// tests (<c>FitTextureSizeMathTests</c>) instead of full texture-import round-trips.
        /// </summary>
        internal static FitComputation ComputeFit(
            int width,
            int height,
            int currentTextureSize,
            FitMode fitMode,
            int minAllowedTextureSize,
            int maxAllowedTextureSize
        )
        {
            FitTextureSizeAPI.FitComputation fit = FitTextureSizeAPI.ComputeFit(
                width,
                height,
                currentTextureSize,
                fitMode,
                minAllowedTextureSize,
                maxAllowedTextureSize
            );
            return new FitComputation(fit.TargetSize, fit.NeedsChange, fit.Grew, fit.Shrank);
        }

        internal int CalculateTextureChanges(bool applyChanges)
        {
            using PooledResource<List<string>> textureGuidLease = Buffers<string>.List.Get(
                out List<string> textureGuids
            );
            if (!CollectAssetGuids(textureGuids))
            {
                return -1;
            }
            if (textureGuids.Count == 0)
            {
                this.Log($"No textures found in the specified paths.");
                return 0;
            }

            FitTextureSizeAPI.Options options = new()
            {
                FitMode = _fitMode,
                OnlySprites = _onlySprites,
                MinAllowedTextureSize = _minAllowedTextureSize,
                MaxAllowedTextureSize = _maxAllowedTextureSize,
                ApplyToStandalone = _applyToStandalone,
                ApplyToAndroid = _applyToAndroid,
                ApplyToiOS = _applyToiOS,
                NameFilter = _nameFilter,
                UseRegexForName = _useRegexForName,
                CaseSensitiveNameFilter = _caseSensitiveNameFilter,
                LabelFilterCsv = _labelFilterCsv,
            };
            FitTextureSizeAPI.Result result;
            try
            {
                result = FitTextureSizeAPI.Run(
                    textureGuids,
                    options,
                    applyChanges,
                    (path, index, total) =>
                        EditorUi.CancelableProgress(
                            applyChanges ? "Fitting Texture Size" : "Calculating Changes",
                            $"Checking: {Path.GetFileName(path)} ({index}/{total})",
                            index / (float)total
                        )
                );
            }
            finally
            {
                EditorUi.ClearProgress();
            }

            if (applyChanges)
            {
                _hasLastRunSummary = true;
                _lastRunTotal = result.Total;
                _lastRunChanged = result.Changed;
                _lastRunGrows = result.Grown;
                _lastRunShrinks = result.Shrunk;
                _lastRunUnchanged = result.Unchanged;
                if (result.Changed == 0)
                {
                    this.Log($"No textures updated.");
                }
                else
                {
                    this.Log($"Updated {result.Changed} textures.");
                }
            }
            _potentialGrowCount = result.Grown;
            _potentialShrinkCount = result.Shrunk;
            _potentialUnchangedCount = result.Unchanged;
            if (result.Cancelled)
            {
                this.LogWarn($"Operation cancelled by user.");
                return -1;
            }
            if (!result.Succeeded)
            {
                this.LogError($"Texture fitting failed: {result.Error}");
                return -1;
            }
            return result.Changed;
        }

        private void OnEnable()
        {
            ReleaseSerializedState();
            _serializedObject = new SerializedObject(this);
            _textureSourcePathsProperty = _serializedObject.FindProperty(
                nameof(_textureSourcePaths)
            );

            if (_textureSourcePaths is { Count: > 0 })
            {
                return;
            }

            _textureSourcePaths ??= new List<Object>();
            if (_textureSourcePaths.Count == 0)
            {
                Object defaultFolder = AssetDatabase.LoadAssetAtPath<Object>("Assets/Sprites");
                if (defaultFolder == null)
                {
                    return;
                }

                _textureSourcePaths.Add(defaultFolder);
                _serializedObject.Update();
            }
        }

        private void OnDisable()
        {
            ReleaseSerializedState();
        }

        private void ReleaseSerializedState()
        {
            _textureSourcePathsProperty = null;
            _serializedObject?.Dispose();
            _serializedObject = null;
        }

        private void OnGUI()
        {
            _serializedObject.Update();
            bool beganScroll = false;
            try
            {
                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
                beganScroll = true;

                EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
                _fitMode = (FitMode)
                    EditorGUILayout.EnumPopup(
                        new GUIContent(
                            "Fit Mode",
                            "GrowAndShrink: set max size to the smallest power-of-two that fits the source dimensions.\nGrowOnly: increase up to next POT if needed, never shrink.\nShrinkOnly: decrease to smallest POT that fits the source, never grow.\nRoundToNearest: choose nearest POT to source size (ties round up)."
                        ),
                        _fitMode
                    );

                string modeHelp = _fitMode switch
                {
                    FitMode.GrowAndShrink => "Grow or shrink to bound POT around the source size.",
                    FitMode.GrowOnly =>
                        "Only increase max size to the next POT if the source exceeds it.",
                    FitMode.ShrinkOnly =>
                        "Only decrease to the tightest POT that still fits the source.",
                    FitMode.RoundToNearest =>
                        "Choose the nearest power-of-two to the source size (ties up).",
                    _ => string.Empty,
                };
                if (!string.IsNullOrEmpty(modeHelp))
                {
                    EditorGUILayout.HelpBox(modeHelp, MessageType.None);
                }

                _useSelectionOnly = EditorGUILayout.Toggle(
                    new GUIContent(
                        "Only Current Selection",
                        "When enabled, only process assets and folders currently selected in the Project window."
                    ),
                    _useSelectionOnly
                );
                _onlySprites = EditorGUILayout.Toggle(
                    new GUIContent(
                        "Only Sprites",
                        "When enabled, only process textures whose importer type is Sprite."
                    ),
                    _onlySprites
                );

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Filters", EditorStyles.boldLabel);
                _nameFilter = EditorGUILayout.TextField(
                    new GUIContent(
                        "Name Filter",
                        "Filter textures by filename (without extension). Leave empty for no name filtering."
                    ),
                    _nameFilter
                );
                _useRegexForName = EditorGUILayout.Toggle(
                    new GUIContent("Use Regex", "Interpret Name Filter as a regular expression."),
                    _useRegexForName
                );
                _caseSensitiveNameFilter = EditorGUILayout.Toggle(
                    new GUIContent(
                        "Case Sensitive",
                        "Apply case-sensitive matching for the Name Filter."
                    ),
                    _caseSensitiveNameFilter
                );
                _labelFilterCsv = EditorGUILayout.TextField(
                    new GUIContent(
                        "Label Filter (CSV)",
                        "Comma-separated list of asset labels. When provided, only assets containing at least one of these labels are processed."
                    ),
                    _labelFilterCsv
                );

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Bounds", EditorStyles.boldLabel);
                _minAllowedTextureSize = Mathf.Clamp(
                    EditorGUILayout.IntField(
                        new GUIContent(
                            "Min Allowed Size",
                            "Lower clamp for computed maxTextureSize. Final applied size will not be less than this value."
                        ),
                        _minAllowedTextureSize
                    ),
                    1,
                    16384
                );
                _maxAllowedTextureSize = Mathf.Clamp(
                    EditorGUILayout.IntField(
                        new GUIContent(
                            "Max Allowed Size",
                            "Upper clamp for computed maxTextureSize. Final applied size will not exceed this value."
                        ),
                        _maxAllowedTextureSize
                    ),
                    _minAllowedTextureSize,
                    16384
                );

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Platform Overrides", EditorStyles.boldLabel);
                _applyToStandalone = EditorGUILayout.Toggle(
                    new GUIContent(
                        "Apply to Standalone",
                        "Also apply computed max size to Standalone platform override."
                    ),
                    _applyToStandalone
                );
                _applyToAndroid = EditorGUILayout.Toggle(
                    new GUIContent(
                        "Apply to Android",
                        "Also apply computed max size to Android platform override."
                    ),
                    _applyToAndroid
                );
                _applyToiOS = EditorGUILayout.Toggle(
                    new GUIContent(
                        "Apply to iOS",
                        "Also apply computed max size to iOS platform override."
                    ),
                    _applyToiOS
                );

                EditorGUILayout.Space();
                PersistentDirectoryGUI.PathSelectorObjectArray(
                    _textureSourcePathsProperty,
                    nameof(FitTextureSizeWindow)
                );
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

                if (GUILayout.Button("Calculate Potential Changes"))
                {
                    _potentialChangeCount = CalculateTextureChanges(applyChanges: false);
                    string message =
                        0 <= _potentialChangeCount
                            ? $"Calculation complete. {_potentialChangeCount} textures would be modified."
                            : "Calculation failed.";
                    this.Log($"{message}");
                }

                if (0 <= _potentialChangeCount)
                {
                    EditorGUILayout.HelpBox(
                        $"{_potentialChangeCount} textures would be modified with the current settings. Grows: {_potentialGrowCount}, Shrinks: {_potentialShrinkCount}, Unchanged: {_potentialUnchangedCount}.",
                        MessageType.Info
                    );
                    if (GUILayout.Button("Copy Summary"))
                    {
                        string summary =
                            $"Fit Texture Size Summary\nMode: {_fitMode}\nOnly Selection: {_useSelectionOnly}\nOnly Sprites: {_onlySprites}\nMin: {_minAllowedTextureSize}, Max: {_maxAllowedTextureSize}\nTotal Changes: {_potentialChangeCount}\nGrows: {_potentialGrowCount}, Shrinks: {_potentialShrinkCount}, Unchanged: {_potentialUnchangedCount}";
                        EditorGUIUtility.systemCopyBuffer = summary;
                        this.Log($"Summary copied to clipboard.");
                    }
                }

                if (_hasLastRunSummary)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Last Run", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox(
                        $"Processed: {_lastRunTotal}. Changed: {_lastRunChanged}. Grows: {_lastRunGrows}. Shrinks: {_lastRunShrinks}. Unchanged: {_lastRunUnchanged}.",
                        MessageType.Info
                    );
                    if (GUILayout.Button("Copy Last Run Summary"))
                    {
                        string runSummary =
                            $"Fit Texture Size Last Run\nMode: {_fitMode}\nOnly Selection: {_useSelectionOnly}\nOnly Sprites: {_onlySprites}\nMin: {_minAllowedTextureSize}, Max: {_maxAllowedTextureSize}\nProcessed: {_lastRunTotal}\nChanged: {_lastRunChanged}\nGrows: {_lastRunGrows}\nShrinks: {_lastRunShrinks}\nUnchanged: {_lastRunUnchanged}";
                        EditorGUIUtility.systemCopyBuffer = runSummary;
                        this.Log($"Last run summary copied to clipboard.");
                    }
                }

                if (GUILayout.Button("Run Fit Texture Size"))
                {
                    int actualChanges = CalculateTextureChanges(applyChanges: true);
                    _potentialChangeCount = -1;
                    string message =
                        0 <= actualChanges
                            ? $"Operation complete. {actualChanges} textures were modified."
                            : "Operation failed.";
                    this.Log($"{message}");
                }
            }
            finally
            {
                if (beganScroll)
                {
                    EditorGUILayout.EndScrollView();
                }
            }
            _serializedObject.ApplyModifiedProperties();
        }

        private bool CollectAssetGuids(List<string> destination)
        {
            _textureSourcePaths ??= new List<Object>();
            using PooledResource<List<string>> pathLease = Buffers<string>.List.Get(
                out List<string> sourcePaths
            );
            bool hadSource = false;
            if (_useSelectionOnly)
            {
                foreach (string guid in Selection.assetGUIDs)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        sourcePaths.Add(path);
                    }
                }
            }
            else
            {
                foreach (Object sourceObject in _textureSourcePaths)
                {
                    if (sourceObject == null)
                    {
                        continue;
                    }
                    hadSource = true;
                    string path = AssetDatabase.GetAssetPath(sourceObject);
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        sourcePaths.Add(path);
                    }
                }
            }

            bool searchAssetsWhenEmpty = !_useSelectionOnly && !hadSource;
            if (searchAssetsWhenEmpty)
            {
                this.Log($"No source folders specified. Searching entire 'Assets' folder.");
            }
            else if (hadSource && sourcePaths.Count == 0)
            {
                this.LogWarn($"No valid source folders found in the list.");
            }
            if (
                FitTextureSizeAPI.TryFindTextures(
                    sourcePaths,
                    _onlySprites,
                    destination,
                    out string error,
                    searchAssetsWhenEmpty
                )
            )
            {
                return true;
            }
            this.LogError($"Texture discovery failed: {error}");
            return false;
        }

        /// <summary>
        /// Pure result of the fit-size computation (no Unity asset I/O), so the
        /// power-of-two / clamp / direction behavior can be unit-tested without importing
        /// textures.
        /// </summary>
        internal readonly struct FitComputation
        {
            public readonly int TargetSize;
            public readonly bool NeedsChange;
            public readonly bool Grew;
            public readonly bool Shrank;

            public FitComputation(int targetSize, bool needsChange, bool grew, bool shrank)
            {
                TargetSize = targetSize;
                NeedsChange = needsChange;
                Grew = grew;
                Shrank = shrank;
            }
        }
    }
#endif
}
