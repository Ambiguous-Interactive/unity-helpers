// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using CustomEditors;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    public sealed class TextureSettingsApplierWindow : EditorWindow
    {
        // Basic importer settings
        public bool applyReadOnly;
        public bool isReadOnly;
        public bool applyMipMaps;
        public bool generateMipMaps;
        public bool applyWrapMode;

        [WShowIf(nameof(applyWrapMode))]
        public TextureWrapMode wrapMode = TextureWrapMode.Clamp;
        public bool applyFilterMode;

        [WShowIf(nameof(applyFilterMode))]
        public FilterMode filterMode = FilterMode.Trilinear;

        // Default Platform Settings
        public TextureImporterCompression compression = TextureImporterCompression.CompressedHQ;
        public bool useCrunchCompression = true;
        public TextureResizeAlgorithm textureResizeAlgorithm = TextureResizeAlgorithm.Bilinear;
        public int maxTextureSize = SetTextureImportData.MaxTextureSize;
        public TextureImporterFormat textureFormat = TextureImporterFormat.Automatic;

        // Sources and filters
        public List<string> spriteFileExtensions = new() { ".png" };
        public List<Texture2D> textures = new();
        public List<Object> directories = new();

        // Optional: named per-platform overrides
        public List<PlatformOverrideEntry> platformOverrides = new();
        private int _addPlatformIndex;
        private readonly Dictionary<int, int> _replaceSelectionByIndex = new();

        // Flow options
        public bool requireChangesBeforeApply = true; // If true, stats are checked and apply is skipped if nothing changes.

        // UI backing
        private SerializedObject _so;
        private SerializedProperty _texturesProp;
        private SerializedProperty _directoriesProp;
        private SerializedProperty _extensionsProp;
        private SerializedProperty _platformOverridesProp;
        private Vector2 _scrollPos;

        // Stats/preview
        private int _totalTexturesToProcess = -1;
        private int _texturesThatWillChange = -1;
        private bool _showPreviewOfChanges;
        private readonly List<string> _assetsThatWillChange = new();
        private readonly TextureImporterSettings _settingsBuffer = new();

        [MenuItem("Tools/Wallstop Studios/Unity Helpers/Texture Settings Applier", priority = -1)]
        public static void ShowWindow()
        {
            TextureSettingsApplierWindow window = GetWindow<TextureSettingsApplierWindow>(
                "Texture Settings Applier"
            );
            window.minSize = new Vector2(450, 320);
            window.Show();
        }

        internal SerializedObject SerializedStateForTesting => _so;

        private void BindSerializedState()
        {
            ReleaseSerializedState();
            _so = new SerializedObject(this);
            _texturesProp = _so.FindProperty(nameof(textures));
            _directoriesProp = _so.FindProperty(nameof(directories));
            _extensionsProp = _so.FindProperty(nameof(spriteFileExtensions));
            _platformOverridesProp = _so.FindProperty(nameof(platformOverrides));
        }

        private void ReleaseSerializedState()
        {
            _texturesProp = null;
            _directoriesProp = null;
            _extensionsProp = null;
            _platformOverridesProp = null;
            _so?.Dispose();
            _so = null;
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
            if (_so == null)
            {
                BindSerializedState();
            }

            _so.Update();
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            EditorGUILayout.LabelField("Texture Sources", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_texturesProp, new GUIContent("Specific Textures"), true);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Directory Sources", EditorStyles.boldLabel);
            PersistentDirectoryGUI.PathSelectorObjectArray(
                _directoriesProp,
                nameof(TextureSettingsApplierWindow)
            );
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(
                _extensionsProp,
                new GUIContent("Texture File Extensions"),
                true
            );

            applyReadOnly = EditorGUILayout.Toggle("Apply Read/Write", applyReadOnly);
            if (applyReadOnly)
            {
                isReadOnly = EditorGUILayout.Toggle("Is Read-Only", isReadOnly);
            }
            applyMipMaps = EditorGUILayout.Toggle("Apply MipMaps", applyMipMaps);
            if (applyMipMaps)
            {
                generateMipMaps = EditorGUILayout.Toggle("Generate MipMaps", generateMipMaps);
            }
            applyWrapMode = EditorGUILayout.Toggle("Apply Wrap Mode", applyWrapMode);
            if (applyWrapMode)
            {
                wrapMode = (TextureWrapMode)EditorGUILayout.EnumPopup("Wrap Mode", wrapMode);
            }
            applyFilterMode = EditorGUILayout.Toggle("Apply Filter Mode", applyFilterMode);
            if (applyFilterMode)
            {
                filterMode = (FilterMode)EditorGUILayout.EnumPopup("Filter Mode", filterMode);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Default Platform Settings", EditorStyles.boldLabel);
            textureResizeAlgorithm = (TextureResizeAlgorithm)
                EditorGUILayout.EnumPopup("Resize Algorithm", textureResizeAlgorithm);
            maxTextureSize = EditorGUILayout.IntField("Max Texture Size", maxTextureSize);
            textureFormat = (TextureImporterFormat)
                EditorGUILayout.EnumPopup("Format", textureFormat);
            compression = (TextureImporterCompression)
                EditorGUILayout.EnumPopup("Compression", compression);
            useCrunchCompression = EditorGUILayout.Toggle(
                "Use Crunch Compression",
                useCrunchCompression
            );

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Platform Overrides", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_platformOverridesProp, true);
            EditorGUILayout.BeginHorizontal();
            string[] knownNames = TexturePlatformNameHelper.GetKnownPlatformNames();
            _addPlatformIndex = EditorGUILayout.Popup(
                "Add Known Platform",
                _addPlatformIndex,
                knownNames
            );
            if (GUILayout.Button("Add Selected"))
            {
                if (0 <= _addPlatformIndex && _addPlatformIndex < knownNames.Length)
                {
                    AddPlatformIfMissing(knownNames[_addPlatformIndex]);
                }
            }
            EditorGUILayout.EndHorizontal();
            for (int i = 0; i < platformOverrides.Count; i++)
            {
                string name = platformOverrides[i]?.platformName?.Trim();
                if (!string.IsNullOrEmpty(name) && Array.IndexOf(knownNames, name) < 0)
                {
                    EditorGUILayout.HelpBox(
                        $"Unknown platform name '{name}'. It will be passed to Unity importer as-is.",
                        MessageType.Info
                    );

                    // Quick fix UX: allow replacing with a known platform directly
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Replace With", GUILayout.Width(90));
                    int currentChoice = 0;
                    if (_replaceSelectionByIndex.TryGetValue(i, out int tmp))
                    {
                        currentChoice = tmp;
                    }
                    currentChoice = EditorGUILayout.Popup(currentChoice, knownNames);
                    _replaceSelectionByIndex[i] = currentChoice;
                    if (GUILayout.Button("Replace", GUILayout.Width(80)))
                    {
                        if (0 <= currentChoice && currentChoice < knownNames.Length)
                        {
                            platformOverrides[i].platformName = knownNames[currentChoice];
                            Repaint();
                        }
                    }
                    // Heuristic quick button for common typo: iOS -> iPhone
                    if (string.Equals(name, "iOS", StringComparison.OrdinalIgnoreCase))
                    {
                        int idx = Array.IndexOf(knownNames, "iPhone");
                        if (0 <= idx && GUILayout.Button("Use iPhone", GUILayout.Width(90)))
                        {
                            platformOverrides[i].platformName = "iPhone";
                            _replaceSelectionByIndex[i] = idx;
                            Repaint();
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            requireChangesBeforeApply = EditorGUILayout.Toggle(
                new GUIContent(
                    "Require Changes Before Apply",
                    "If checked, apply will be skipped when no assets would change."
                ),
                requireChangesBeforeApply
            );
            if (requireChangesBeforeApply && 0 <= _totalTexturesToProcess)
            {
                if (_texturesThatWillChange == 0)
                {
                    EditorGUILayout.HelpBox(
                        "No textures require changes; Apply will be skipped.",
                        MessageType.Info
                    );
                }
                else if (0 < _texturesThatWillChange)
                {
                    EditorGUILayout.HelpBox(
                        $"{_texturesThatWillChange} texture(s) will change.",
                        MessageType.None
                    );
                }
            }
            if (GUILayout.Button("Calculate Stats"))
            {
                CalculateStats();
            }
            if (0 <= _totalTexturesToProcess && 0 <= _texturesThatWillChange)
            {
                EditorGUILayout.LabelField($"Textures to process: {_totalTexturesToProcess}");
                EditorGUILayout.LabelField($"Textures that will change: {_texturesThatWillChange}");
                _showPreviewOfChanges = EditorGUILayout.Foldout(
                    _showPreviewOfChanges,
                    $"Preview ({_assetsThatWillChange.Count})"
                );
                if (_showPreviewOfChanges)
                {
                    int toShow = Mathf.Min(_assetsThatWillChange.Count, 200);
                    for (int i = 0; i < toShow; i++)
                    {
                        EditorGUILayout.LabelField(_assetsThatWillChange[i]);
                    }
                    if (200 < _assetsThatWillChange.Count)
                    {
                        EditorGUILayout.LabelField(
                            $"...and {_assetsThatWillChange.Count - 200} more"
                        );
                    }
                    if (GUILayout.Button("Copy List"))
                    {
                        EditorGUIUtility.systemCopyBuffer = string.Join(
                            "\n",
                            _assetsThatWillChange
                        );
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField("Press 'Calculate Stats' to see processing details.");
            }

            if (GUILayout.Button("Apply Settings to Textures"))
            {
                ApplySettings();
            }

            EditorGUILayout.EndScrollView();
            _so.ApplyModifiedProperties();
        }

        private void AddPlatformIfMissing(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            for (int i = 0; i < (platformOverrides?.Count ?? 0); i++)
            {
                PlatformOverrideEntry p = platformOverrides[i];
                string existing = p?.platformName;
                if (
                    !string.IsNullOrEmpty(existing)
                    && string.Equals(existing.Trim(), name, StringComparison.Ordinal)
                )
                {
                    return;
                }
            }
            platformOverrides.Add(new PlatformOverrideEntry { platformName = name });
        }

        private TextureSettingsApplierAPI.Config BuildConfig()
        {
            TextureSettingsApplierAPI.Config config = new()
            {
                applyReadWriteEnabled = applyReadOnly,
                readWriteEnabled = !isReadOnly,
                applyMipMaps = applyMipMaps,
                generateMipMaps = generateMipMaps,
                applyWrapMode = applyWrapMode,
                wrapMode = wrapMode,
                applyFilterMode = applyFilterMode,
                filterMode = filterMode,
                applyPlatformResizeAlgorithm = true,
                platformResizeAlgorithm = textureResizeAlgorithm,
                applyPlatformMaxTextureSize = true,
                platformMaxTextureSize = maxTextureSize,
                applyPlatformFormat = true,
                platformFormat = textureFormat,
                applyPlatformCompression = true,
                platformCompression = compression,
                applyPlatformCrunchCompression = true,
                platformUseCrunchCompression = useCrunchCompression,
                applyCompression = false,
                applyCrunchCompression = false,
            };
            if (platformOverrides is { Count: > 0 })
            {
                TextureSettingsApplierAPI.PlatformOverride[] arr =
                    new TextureSettingsApplierAPI.PlatformOverride[platformOverrides.Count];
                for (int i = 0; i < platformOverrides.Count; i++)
                {
                    PlatformOverrideEntry e = platformOverrides[i];
                    arr[i] = new TextureSettingsApplierAPI.PlatformOverride
                    {
                        name = string.IsNullOrWhiteSpace(e.platformName)
                            ? string.Empty
                            : e.platformName.Trim(),
                        applyResizeAlgorithm = e.applyResizeAlgorithm,
                        resizeAlgorithm = e.resizeAlgorithm,
                        applyMaxTextureSize = e.applyMaxTextureSize,
                        maxTextureSize = e.maxTextureSize,
                        applyFormat = e.applyFormat,
                        format = e.format,
                        applyCompression = e.applyCompression,
                        compression = e.compression,
                        applyCrunchCompression = e.applyCrunchCompression,
                        useCrunchCompression = e.useCrunchCompression,
                    };
                }
                config.platformOverrides = arr;
            }
            return config;
        }

        private void GetTargetTexturePaths(List<string> destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            destination.Clear();
            // Build extension filter (normalize)
            using (
                SetBuffers<string>
                    .GetHashSetPool(StringComparer.OrdinalIgnoreCase)
                    .Get(out HashSet<string> allowedExtensions)
            )
            {
                if (spriteFileExtensions != null)
                {
                    foreach (string extRaw in spriteFileExtensions)
                    {
                        if (string.IsNullOrWhiteSpace(extRaw))
                        {
                            continue;
                        }

                        string ext = extRaw.StartsWith(".") ? extRaw : "." + extRaw;
                        _ = allowedExtensions.Add(ext);
                    }
                }

                // Collect folders
                using PooledResource<List<string>> folderAssetPathsResource =
                    Buffers<string>.List.Get(out List<string> folderAssetPaths);
                {
                    if (directories != null)
                    {
                        foreach (Object directory in directories)
                        {
                            if (directory == null)
                            {
                                continue;
                            }

                            string assetPath = AssetDatabase.GetAssetPath(directory);
                            if (
                                !string.IsNullOrWhiteSpace(assetPath)
                                && AssetDatabase.IsValidFolder(assetPath)
                            )
                            {
                                folderAssetPaths.Add(assetPath);
                            }
                        }
                    }

                    using (
                        SetBuffers<string>
                            .GetHashSetPool(StringComparer.OrdinalIgnoreCase)
                            .Get(out HashSet<string> unique)
                    )
                    {
                        if (0 < folderAssetPaths.Count)
                        {
                            string[] guids = AssetDatabase.FindAssets(
                                "t:Texture2D",
                                folderAssetPaths.ToArray()
                            );
                            for (int i = 0; i < guids.Length; i++)
                            {
                                string p = AssetDatabase.GUIDToAssetPath(guids[i]);
                                if (string.IsNullOrWhiteSpace(p))
                                {
                                    continue;
                                }

                                string ext = Path.GetExtension(p);
                                if (0 < allowedExtensions.Count && !allowedExtensions.Contains(ext))
                                {
                                    continue;
                                }

                                _ = unique.Add(p);
                            }
                        }

                        // De-dupe textures and skip nulls without LINQ
                        using (Buffers<Texture2D>.HashSet.Get(out HashSet<Texture2D> texSet))
                        {
                            if (textures == null)
                            {
                                foreach (string path in unique)
                                {
                                    destination.Add(path);
                                }
                                return;
                            }

                            for (int ti = 0; ti < textures.Count; ti++)
                            {
                                Texture2D t = textures[ti];
                                if (t == null)
                                {
                                    continue;
                                }
                                if (!texSet.Add(t))
                                {
                                    continue;
                                }

                                string p = AssetDatabase.GetAssetPath(t);
                                if (string.IsNullOrWhiteSpace(p))
                                {
                                    continue;
                                }

                                string ext = Path.GetExtension(p);
                                if (0 < allowedExtensions.Count && !allowedExtensions.Contains(ext))
                                {
                                    continue;
                                }

                                _ = unique.Add(p);
                            }
                        }

                        foreach (string path in unique)
                        {
                            destination.Add(path);
                        }
                    }
                }
            }
        }

        public void CalculateStats()
        {
            using PooledResource<List<string>> targetsLease = Buffers<string>.List.Get(
                out List<string> targets
            );
            GetTargetTexturePaths(targets);
            _totalTexturesToProcess = targets.Count;
            _texturesThatWillChange = 0;
            _assetsThatWillChange.Clear();
            if (_assetsThatWillChange.Capacity < targets.Count)
            {
                _assetsThatWillChange.Capacity = targets.Count;
            }

            TextureSettingsApplierAPI.Config config = BuildConfig();
            double last = EditorApplication.timeSinceStartup;
            for (int i = 0; i < targets.Count; i++)
            {
                string rel = targets[i];
                double now = EditorApplication.timeSinceStartup;
                if (i == 0 || i == targets.Count - 1 || i % 50 == 0 || 0.2 < now - last)
                {
                    Utils.EditorUi.ShowProgress(
                        "Calculating Stats",
                        $"Checking '{Path.GetFileName(rel)}' ({i + 1}/{_totalTexturesToProcess})",
                        (float)(i + 1) / Math.Max(1, _totalTexturesToProcess)
                    );
                    last = now;
                }
                if (
                    TextureSettingsApplierAPI.WillTextureSettingsChange(
                        rel,
                        in config,
                        _settingsBuffer
                    )
                )
                {
                    _texturesThatWillChange++;
                    _assetsThatWillChange.Add(rel);
                }
            }
            Utils.EditorUi.ClearProgress();
        }

        public void ApplySettings()
        {
            // Dry-run behavior: if asked to require changes, compute stats and return early when none.
            if (
                requireChangesBeforeApply
                && (_totalTexturesToProcess < 0 || _texturesThatWillChange < 0)
            )
            {
                CalculateStats();
            }
            if (requireChangesBeforeApply && _texturesThatWillChange == 0)
            {
                this.Log($"No textures require changes. Skipping apply.");
                return;
            }

            using PooledResource<List<string>> targetsLease = Buffers<string>.List.Get(
                out List<string> targets
            );
            GetTargetTexturePaths(targets);
            TextureSettingsApplierAPI.Config config = BuildConfig();
            // Warn about unknown platforms prior to apply
            if (platformOverrides != null)
            {
                string[] knownNames = TexturePlatformNameHelper.GetKnownPlatformNames();
                for (int i = 0; i < platformOverrides.Count; i++)
                {
                    string name = platformOverrides[i]?.platformName?.Trim();
                    if (
                        !string.IsNullOrEmpty(name)
                        && Array.IndexOf(knownNames, name) < 0
                        && !string.Equals(
                            name,
                            TexturePlatformNameHelper.DefaultPlatformName,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        this.LogWarn(
                            $"Unknown texture platform '{name}'. Settings will be applied as-is."
                        );
                    }
                }
            }
            int count = 0;
            using PooledResource<List<TextureImporter>> changedResource =
                Buffers<TextureImporter>.List.Get(out List<TextureImporter> changed);
            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
            {
                double lastUpdate = EditorApplication.timeSinceStartup;
                for (int i = 0; i < targets.Count; i++)
                {
                    string path = targets[i];
                    double now = EditorApplication.timeSinceStartup;
                    bool shouldUpdate =
                        i == 0 || i == targets.Count - 1 || i % 50 == 0 || 0.2 < now - lastUpdate;
                    if (
                        shouldUpdate
                        && EditorUi.CancelableProgress(
                            "Applying Texture Settings",
                            $"Processing '{Path.GetFileName(path)}' ({i + 1}/{targets.Count})",
                            (float)(i + 1) / Math.Max(1, targets.Count)
                        )
                    )
                    {
                        break;
                    }
                    if (shouldUpdate)
                    {
                        lastUpdate = now;
                    }

                    if (
                        TextureSettingsApplierAPI.TryUpdateTextureSettings(
                            path,
                            in config,
                            out TextureImporter importer,
                            _settingsBuffer
                        )
                    )
                    {
                        if (importer != null)
                        {
                            changed.Add(importer);
                            ++count;
                        }
                    }
                }
            }
            EditorUi.ClearProgress();
            for (int j = 0; j < changed.Count; j++)
            {
                changed[j].SaveAndReimport();
            }

            if (0 < count)
            {
                this.Log($"Processed {count} textures.");
            }
            else
            {
                this.Log($"No textures required changes.");
            }
            if (0 < count)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            // Reset stats to force recalculation next time
            _totalTexturesToProcess = -1;
            _texturesThatWillChange = -1;
        }

        [Serializable]
        public sealed class PlatformOverrideEntry
        {
            public string platformName = TexturePlatformNameHelper.DefaultPlatformName; // DefaultTexturePlatform, Standalone, iPhone, Android, WebGL, etc.

            public bool applyResizeAlgorithm;

            [WShowIf(nameof(applyResizeAlgorithm))]
            public TextureResizeAlgorithm resizeAlgorithm = TextureResizeAlgorithm.Bilinear;

            public bool applyMaxTextureSize;

            [WShowIf(nameof(applyMaxTextureSize))]
            public int maxTextureSize = SetTextureImportData.MaxTextureSize;

            public bool applyFormat;

            [WShowIf(nameof(applyFormat))]
            public TextureImporterFormat format = TextureImporterFormat.Automatic;

            public bool applyCompression;

            [WShowIf(nameof(applyCompression))]
            public TextureImporterCompression compression = TextureImporterCompression.Compressed;

            public bool applyCrunchCompression;

            [WShowIf(nameof(applyCrunchCompression))]
            public bool useCrunchCompression;

            internal static class SerializedPropertyNames
            {
                internal const string PlatformName = nameof(platformName);
                internal const string ApplyResizeAlgorithm = nameof(applyResizeAlgorithm);
                internal const string ResizeAlgorithm = nameof(resizeAlgorithm);
                internal const string ApplyMaxTextureSize = nameof(applyMaxTextureSize);
                internal const string MaxTextureSize = nameof(maxTextureSize);
                internal const string ApplyFormat = nameof(applyFormat);
                internal const string Format = nameof(format);
                internal const string ApplyCompression = nameof(applyCompression);
                internal const string Compression = nameof(compression);
                internal const string ApplyCrunchCompression = nameof(applyCrunchCompression);
                internal const string UseCrunchCompression = nameof(useCrunchCompression);
            }
        }
    }
#endif
}
