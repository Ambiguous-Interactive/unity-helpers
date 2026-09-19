// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.RegularExpressions;
    using UnityEditor;
    using UnityEditor.U2D;
    using UnityEngine;
    using UnityEngine.U2D;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Extensions;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Editor window for managing <c>ScriptableSpriteAtlas</c> configuration assets and generating
    /// corresponding <c>.spriteatlas</c> assets. Supports scanning for adds/removals, bulk generate,
    /// and optional packing after generation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Problems this solves: keeping <c>SpriteAtlas</c> assets in sync with curated rules and sets
    /// of sprites, with visibility into pending additions/removals before writing.
    /// </para>
    /// <para>
    /// How it works: loads all <c>ScriptableSpriteAtlas</c> assets in the project, caches scan
    /// results (to add/remove), and drives generation of the <c>.spriteatlas</c> assets. Optionally
    /// invokes <see cref="SpriteAtlasUtility.PackAllAtlases"/> to repack.
    /// </para>
    /// <para>
    /// Usage: open via menu, refresh config list, create new configs in a target folder, then
    /// Generate/Pack as needed.
    /// </para>
    /// <para>
    /// Caveats: generation modifies/creates assets; ensure correct output paths and VCS.
    /// </para>
    /// </remarks>
    public sealed class ScriptableSpriteAtlasEditor : EditorWindow
    {
        private const string ScriptPropertyPath = "m_Script";

        private const string NewAtlasConfigDirectory = "Assets/Data";

        private static bool SuppressUserPrompts { get; set; }

        private static readonly Comparison<AtlasConfigSortEntry> AtlasConfigNameComparison =
            CompareAtlasConfigNames;

        private readonly Dictionary<ScriptableSpriteAtlas, SerializedObject> _serializedConfigs =
            new();
        private List<ScriptableSpriteAtlas> _atlasConfigs = new();
        private Vector2 _scrollPosition;
        private bool _packAfterGenerate;

        private readonly Dictionary<ScriptableSpriteAtlas, ScanResult> _scanResultsCache = new();
        private readonly Dictionary<ScriptableSpriteAtlas, bool> _foldoutStates = new();

        static ScriptableSpriteAtlasEditor()
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

        [MenuItem("Tools/Wallstop Studios/Unity Helpers/Sprite Atlas Generator")]
        public static void ShowWindow()
        {
            GetWindow<ScriptableSpriteAtlasEditor>("Sprite Atlas Generator");
        }

        internal static void SortAtlasConfigs(List<ScriptableSpriteAtlas> configs)
        {
            if (configs == null || configs.Count < 2)
            {
                return;
            }

            using PooledResource<List<AtlasConfigSortEntry>> lease =
                Buffers<AtlasConfigSortEntry>.GetList(
                    configs.Count,
                    out List<AtlasConfigSortEntry> entries
                );
            for (int index = 0; index < configs.Count; ++index)
            {
                entries.Add(new AtlasConfigSortEntry(configs[index], index));
            }

            entries.Sort(AtlasConfigNameComparison);
            for (int index = 0; index < entries.Count; ++index)
            {
                configs[index] = entries[index].Config;
            }
        }

        internal static int CountValidSprites(IReadOnlyList<Sprite> sprites)
        {
            if (sprites == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < sprites.Count; ++i)
            {
                if (sprites[i] != null)
                {
                    ++count;
                }
            }

            return count;
        }

        internal static void AppendSpritesWithTextures(
            IReadOnlyList<Sprite> sprites,
            List<Sprite> destination
        )
        {
            if (sprites == null || destination == null)
            {
                return;
            }

            for (int i = 0; i < sprites.Count; ++i)
            {
                Sprite sprite = sprites[i];
                if (sprite != null && sprite.texture != null)
                {
                    destination.Add(sprite);
                }
            }
        }

        internal static Object[] ToObjectArray(IReadOnlyList<Sprite> sprites)
        {
            if (sprites == null || sprites.Count == 0)
            {
                return Array.Empty<Object>();
            }

            Object[] result = new Object[sprites.Count];
            for (int i = 0; i < sprites.Count; ++i)
            {
                result[i] = sprites[i];
            }

            return result;
        }

        internal static void InitializeSourceFolderEntry(SerializedProperty entry)
        {
            if (entry == null)
            {
                return;
            }

            entry.FindPropertyRelative(nameof(SourceFolderEntry.selectionMode)).intValue = (int)
                SpriteSelectionMode.Regex;
            entry.FindPropertyRelative(nameof(SourceFolderEntry.labelSelectionMode)).intValue =
                (int)LabelSelectionMode.All;
            entry.FindPropertyRelative(nameof(SourceFolderEntry.regexAndTagLogic)).intValue = (int)
                SpriteSelectionBooleanLogic.And;
            entry
                .FindPropertyRelative(nameof(SourceFolderEntry.excludeLabelSelectionMode))
                .intValue = (int)LabelSelectionMode.AnyOf;
            entry.FindPropertyRelative(nameof(SourceFolderEntry.regexes)).arraySize = 0;
            entry.FindPropertyRelative(nameof(SourceFolderEntry.labels)).arraySize = 0;
            entry.FindPropertyRelative(nameof(SourceFolderEntry.excludeRegexes)).arraySize = 0;
            entry.FindPropertyRelative(nameof(SourceFolderEntry.excludeLabels)).arraySize = 0;
            entry.FindPropertyRelative(nameof(SourceFolderEntry.excludePathPrefixes)).arraySize = 0;
        }

        private static int CompareAtlasConfigNames(
            AtlasConfigSortEntry left,
            AtlasConfigSortEntry right
        )
        {
            string leftName = left.Config != null ? left.Config.name : null;
            string rightName = right.Config != null ? right.Config.name : null;
            int nameComparison = Comparer<string>.Default.Compare(leftName, rightName);
            return nameComparison != 0
                ? nameComparison
                : left.OriginalIndex.CompareTo(right.OriginalIndex);
        }

        private static void PingAtlas(string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return;
            }
            Object obj = AssetDatabase.LoadAssetAtPath<Object>(outputPath);
            if (obj != null)
            {
                Selection.activeObject = obj;
                EditorGUIUtility.PingObject(obj);
            }
        }

        private static void RevealInExplorer(string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                return;
            }
            EditorUtility.RevealInFinder(outputPath);
        }

        internal SerializedObject GetSerializedConfig(ScriptableSpriteAtlas config)
        {
            return _serializedConfigs.GetOrAdd(
                config,
                static newConfig => new SerializedObject(newConfig)
            );
        }

        internal void LoadAtlasConfigs()
        {
            _atlasConfigs.Clear();
            Dictionary<ScriptableSpriteAtlas, ScanResult> existingScanCache = new(
                _scanResultsCache
            );
            ReleaseSerializedConfigs();
            _scanResultsCache.Clear();

            string[] guids = AssetDatabase.FindAssets("t:ScriptableSpriteAtlas");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }
                ScriptableSpriteAtlas config = AssetDatabase.LoadAssetAtPath<ScriptableSpriteAtlas>(
                    path
                );
                if (config != null)
                {
                    _atlasConfigs.Add(config);
                    if (existingScanCache.TryGetValue(config, out ScanResult cachedResult))
                    {
                        _scanResultsCache[config] = cachedResult;
                    }
                    else
                    {
                        _scanResultsCache.TryAdd(config, _ => new ScanResult());
                    }
                    _ = GetSerializedConfig(config);
                    _foldoutStates.TryAdd(config, true);
                }
            }
            SortAtlasConfigs(_atlasConfigs);
            using (Buffers<ScriptableSpriteAtlas>.List.Get(out List<ScriptableSpriteAtlas> removed))
            {
                foreach (ScriptableSpriteAtlas config in _foldoutStates.Keys)
                {
                    if (!_serializedConfigs.ContainsKey(config))
                    {
                        removed.Add(config);
                    }
                }
                foreach (ScriptableSpriteAtlas config in removed)
                {
                    _ = _foldoutStates.Remove(config);
                }
            }
        }

        internal void GenerateAllAtlases()
        {
            if (_atlasConfigs.Count == 0)
            {
                Utils.EditorUi.Info(
                    "No Configurations",
                    "No ScriptableSpriteAtlas configurations found to generate."
                );
                return;
            }

            int totalConfigs = _atlasConfigs.Count;
            int currentConfig = 0;
            bool changed = false;

            try
            {
                using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
                {
                    foreach (ScriptableSpriteAtlas config in _atlasConfigs)
                    {
                        if (config == null)
                        {
                            continue;
                        }

                        currentConfig++;
                        float progress = (float)currentConfig / totalConfigs;
                        Utils.EditorUi.ShowProgress(
                            "Generating Sprite Atlases",
                            $"Processing: {config.name}",
                            progress
                        );
                        changed |= ScriptableSpriteAtlasGenerator.GenerateWithoutRefresh(config);
                    }
                }
            }
            finally
            {
                Utils.EditorUi.ClearProgress();
            }
            if (changed)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        internal void PackAllProjectAtlases()
        {
            ScriptableSpriteAtlasGenerator.PackAll(EditorUserBuildSettings.activeBuildTarget);
        }

        internal void ForceUncompressedSourceSprites(ScriptableSpriteAtlas config)
        {
            if (config == null)
            {
                return;
            }

            using PooledResource<List<Sprite>> spritesToProcessLease = Buffers<Sprite>.List.Get(
                out List<Sprite> spritesToProcess
            );
            AppendSpritesWithTextures(config.spritesToPack, spritesToProcess);
            if (spritesToProcess.Count == 0)
            {
                this.LogWarn(
                    $"'{config.name}': No valid sprites with textures in the list to modify."
                );
                Utils.EditorUi.Info(
                    "No Sprites",
                    "No valid sprites found in the configuration's list to process."
                );
                return;
            }

            int modifiedCount = 0;
            int errorCount = 0;
            using PooledResource<HashSet<string>> processedAssetPathsLease =
                Buffers<string>.HashSet.Get(out HashSet<string> processedAssetPaths);
            using PooledResource<List<TextureImporter>> importersLease =
                Buffers<TextureImporter>.List.Get(out List<TextureImporter> importers);
            {
                try
                {
                    using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
                    {
                        for (int i = 0; i < spritesToProcess.Count; ++i)
                        {
                            Sprite sprite = spritesToProcess[i];
                            Utils.EditorUi.ShowProgress(
                                "Modifying Source Sprite Import Settings",
                                $"Processing: {sprite.name} ({i + 1}/{spritesToProcess.Count})",
                                (float)(i + 1) / spritesToProcess.Count
                            );

                            string assetPath = AssetDatabase.GetAssetPath(sprite.texture);
                            if (string.IsNullOrWhiteSpace(assetPath))
                            {
                                this.LogWarn(
                                    $"Could not find asset path for sprite's texture: {sprite.name}. Skipping."
                                );
                                errorCount++;
                                continue;
                            }

                            if (!processedAssetPaths.Add(assetPath))
                            {
                                continue;
                            }

                            TextureImporter importer =
                                AssetImporter.GetAtPath(assetPath) as TextureImporter;
                            if (importer == null)
                            {
                                this.LogWarn(
                                    $"Could not get TextureImporter for asset: {assetPath} (from sprite: {sprite.name}). Skipping."
                                );
                                errorCount++;
                                continue;
                            }

                            bool undoRecorded = false;
                            bool settingsActuallyModified = false;

                            void EnsureUndoRecorded()
                            {
                                if (undoRecorded)
                                {
                                    return;
                                }
                                Undo.RecordObject(importer, "Set Sprite Texture To Uncompressed");
                                undoRecorded = true;
                            }

                            if (importer.crunchedCompression)
                            {
                                EnsureUndoRecorded();
                                importer.crunchedCompression = false;
                                settingsActuallyModified = true;
                            }

                            if (
                                importer.textureCompression
                                != TextureImporterCompression.Uncompressed
                            )
                            {
                                EnsureUndoRecorded();
                                importer.textureCompression =
                                    TextureImporterCompression.Uncompressed;
                                settingsActuallyModified = true;
                            }

                            TextureImporterPlatformSettings platformSettings =
                                importer.GetDefaultPlatformTextureSettings();
                            bool platformSettingsChangedThisTime = false;
                            TextureImporterFormat targetFormat =
                                importer.DoesSourceTextureHaveAlpha()
                                    ? TextureImporterFormat.RGBA32
                                    : TextureImporterFormat.RGB24;

                            if (platformSettings.format != targetFormat)
                            {
                                platformSettings.format = targetFormat;
                                platformSettingsChangedThisTime = true;
                            }
                            if (platformSettings.crunchedCompression)
                            {
                                platformSettings.crunchedCompression = false;
                                platformSettingsChangedThisTime = true;
                            }
                            if (platformSettings.compressionQuality != 100)
                            {
                                platformSettings.compressionQuality = 100;
                                platformSettingsChangedThisTime = true;
                            }

                            if (platformSettingsChangedThisTime || !platformSettings.overridden)
                            {
                                EnsureUndoRecorded();
                                platformSettings.overridden = true;
                                importer.SetPlatformTextureSettings(platformSettings);
                                settingsActuallyModified = true;
                            }

                            if (settingsActuallyModified)
                            {
                                importer.SaveAndReimport();
                                importers.Add(importer);
                                modifiedCount++;
                                this.Log(
                                    $"Set import settings for texture: {assetPath} (from sprite: {sprite.name}) to uncompressed ({targetFormat})."
                                );
                            }
                        }
                    }
                }
                finally
                {
                    Utils.EditorUi.ClearProgress();
                }

                foreach (TextureImporter importer in importers)
                {
                    importer.SaveAndReimport();
                }

                if (0 < modifiedCount || 0 < errorCount)
                {
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }

                string summaryMessage =
                    $"Finished processing source sprite textures for '{config.name}'.\n"
                    + $"Successfully modified importers for: {modifiedCount} textures.\n"
                    + $"Errors/Skipped duplicates: {errorCount + (spritesToProcess.Count - processedAssetPaths.Count)}.";
                this.Log($"{summaryMessage}");
            }
        }

        internal void SyncListToScanResult(ScriptableSpriteAtlas config, ScanResult result)
        {
            if (!TryRefreshScanResult(config, result))
            {
                return;
            }
            if (
                ScriptableSpriteAtlasGenerator.Synchronize(
                    config,
                    result.spritesToAdd,
                    result.spritesToRemove,
                    removeUnmatchedSprites: true
                )
            )
            {
                ScanFoldersForConfig(config);
            }
        }

        private void OnEnable()
        {
            LoadAtlasConfigs();
        }

        private void OnProjectChange()
        {
            LoadAtlasConfigs();
        }

        private void OnDisable()
        {
            ReleaseSerializedConfigs();
            _atlasConfigs.Clear();
            _scanResultsCache.Clear();
            _foldoutStates.Clear();
        }

        private void ReleaseSerializedConfigs()
        {
            foreach (SerializedObject serialized in _serializedConfigs.Values)
            {
                serialized?.Dispose();
            }
            _serializedConfigs.Clear();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Sprite Atlas Generation Tool", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh Config List", GUILayout.Height(30)))
                {
                    LoadAtlasConfigs();
                }

                if (
                    GUILayout.Button(
                        $"Create New Config in '{NewAtlasConfigDirectory}'",
                        GUILayout.Height(30)
                    )
                )
                {
                    CreateNewScriptableSpriteAtlas();
                }
            }

            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                _packAfterGenerate = EditorGUILayout.ToggleLeft(
                    new GUIContent(
                        "Pack after generate",
                        "If enabled, atlases will be packed immediately after generation."
                    ),
                    _packAfterGenerate,
                    GUILayout.Width(180)
                );

                if (
                    GUILayout.Button(
                        "Generate/Update All .spriteatlas Assets",
                        GUILayout.Height(30)
                    )
                )
                {
                    GenerateAllAtlases();
                    if (_packAfterGenerate)
                    {
                        PackAllProjectAtlases();
                    }
                }

                if (GUILayout.Button("Generate + Pack All", GUILayout.Height(30)))
                {
                    GenerateAllAtlases();
                    PackAllProjectAtlases();
                }
            }

            if (GUILayout.Button("Pack All Generated Sprite Atlases", GUILayout.Height(40)))
            {
                PackAllProjectAtlases();
            }
            EditorGUILayout.Space(20);
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            if (_atlasConfigs.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No ScriptableSpriteAtlas configurations found. Use the 'Create New Config' button above or create one via Assets > Create > Wallstop Studios > Unity Helpers > Scriptable Sprite Atlas Config.",
                    MessageType.Info
                );
            }

            foreach (ScriptableSpriteAtlas config in _atlasConfigs)
            {
                if (config == null)
                {
                    LoadAtlasConfigs();
                    Repaint();
                    return;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                string foldoutLabel =
                    $"{config.name} (Output: {config.FullOutputPath ?? "Path Not Set"})";
                if (AssetDatabase.Contains(config))
                {
                    foldoutLabel += $" - Path: {AssetDatabase.GetAssetPath(config)}";
                }

                if (!_foldoutStates.TryGetValue(config, out bool expanded))
                {
                    expanded = true;
                }

                expanded = EditorGUILayout.Foldout(
                    expanded,
                    foldoutLabel,
                    true,
                    EditorStyles.foldoutHeader
                );
                _foldoutStates[config] = expanded;

                if (expanded)
                {
                    using EditorGUI.IndentLevelScope indentScope = new();
                    SerializedObject serializedConfig = GetSerializedConfig(config);
                    serializedConfig.Update();
                    EditorGUI.BeginChangeCheck();

                    string currentAssetName = config.name;
                    Rect nameRect = EditorGUILayout.GetControlRect();
                    EditorGUI.BeginChangeCheck();
                    string newAssetName = EditorGUI.TextField(
                        new Rect(nameRect.x, nameRect.y, nameRect.width - 60, nameRect.height),
                        "Asset Name",
                        currentAssetName
                    );
                    if (EditorGUI.EndChangeCheck())
                    {
                        serializedConfig.ApplyModifiedProperties();
                        if (
                            !string.IsNullOrWhiteSpace(newAssetName)
                            && !string.Equals(
                                newAssetName,
                                currentAssetName,
                                System.StringComparison.Ordinal
                            )
                            && AssetDatabase.Contains(config)
                        )
                        {
                            string assetPath = AssetDatabase.GetAssetPath(config);
                            string error = AssetDatabase.RenameAsset(assetPath, newAssetName);
                            if (string.IsNullOrWhiteSpace(error))
                            {
                                LoadAtlasConfigs();
                                GUIUtility.ExitGUI();
                            }
                            else
                            {
                                this.LogError($"Failed to rename asset: {error}");
                            }
                        }
                    }

                    SerializedProperty scriptProperty = serializedConfig.FindProperty(
                        ScriptPropertyPath
                    );
                    if (scriptProperty != null)
                    {
                        GUI.enabled = false;
                        EditorGUILayout.PropertyField(scriptProperty);
                        GUI.enabled = true;
                    }

                    SerializedProperty property = serializedConfig.GetIterator();
                    bool enterChildren = true;
                    while (property.NextVisible(enterChildren))
                    {
                        enterChildren = false;
                        if (
                            string.Equals(
                                property.name,
                                ScriptPropertyPath,
                                StringComparison.Ordinal
                            )
                        )
                        {
                            continue;
                        }

                        EditorGUILayout.PropertyField(property, true);
                    }

                    if (EditorGUI.EndChangeCheck())
                    {
                        serializedConfig.ApplyModifiedProperties();
                    }

                    EditorGUILayout.Space();

                    string fullOutputPath = config.FullOutputPath;
                    if (
                        string.IsNullOrWhiteSpace(config.outputSpriteAtlasDirectory)
                        || string.IsNullOrWhiteSpace(config.outputSpriteAtlasName)
                    )
                    {
                        EditorGUILayout.HelpBox(
                            "Output directory and file name must be set to generate the atlas.",
                            MessageType.Warning
                        );
                    }
                    else
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Button("Generate Only"))
                            {
                                GenerateSingleAtlas(config);
                                if (_packAfterGenerate)
                                {
                                    PackAllProjectAtlases();
                                }
                            }
                            if (GUILayout.Button("Generate + Pack"))
                            {
                                GenerateSingleAtlas(config);
                                PackAllProjectAtlases();
                            }

                            if (!string.IsNullOrWhiteSpace(fullOutputPath))
                            {
                                SpriteAtlas existing = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(
                                    fullOutputPath
                                );
                                if (existing != null)
                                {
                                    if (GUILayout.Button("Ping Atlas"))
                                    {
                                        PingAtlas(fullOutputPath);
                                    }
                                    if (GUILayout.Button("Reveal In Explorer"))
                                    {
                                        RevealInExplorer(fullOutputPath);
                                    }
                                }
                            }
                        }
                    }
                    if (GUILayout.Button("Add New Source Folder Entry"))
                    {
                        string folderPath = Utils.EditorUi.OpenFolderPanel(
                            "Select Source Folder",
                            Application.dataPath,
                            ""
                        );
                        if (!string.IsNullOrWhiteSpace(folderPath))
                        {
                            if (folderPath.StartsWith(Application.dataPath))
                            {
                                string relativePath =
                                    "Assets" + folderPath.Substring(Application.dataPath.Length);
                                relativePath = relativePath.SanitizePath();
                                SerializedProperty sourceFolderEntriesProp =
                                    serializedConfig.FindProperty(
                                        nameof(ScriptableSpriteAtlas.sourceFolderEntries)
                                    );

                                bool pathExists = false;
                                for (int j = 0; j < sourceFolderEntriesProp.arraySize; j++)
                                {
                                    SerializedProperty entryProp =
                                        sourceFolderEntriesProp.GetArrayElementAtIndex(j);
                                    SerializedProperty pathProp = entryProp.FindPropertyRelative(
                                        nameof(SourceFolderEntry.folderPath)
                                    );
                                    if (
                                        string.Equals(
                                            pathProp.stringValue,
                                            relativePath,
                                            StringComparison.Ordinal
                                        )
                                    )
                                    {
                                        pathExists = true;
                                        this.LogWarn(
                                            $"Folder path '{relativePath}' already exists in an entry for '{config.name}'."
                                        );
                                        break;
                                    }
                                }

                                if (!pathExists)
                                {
                                    SerializedProperty newEntryProp =
                                        sourceFolderEntriesProp.AppendArrayElement();
                                    InitializeSourceFolderEntry(newEntryProp);
                                    newEntryProp
                                        .FindPropertyRelative(nameof(SourceFolderEntry.folderPath))
                                        .stringValue = relativePath;

                                    serializedConfig.ApplyModifiedProperties();
                                    this.Log(
                                        $"Added new source folder entry for '{relativePath}' to '{config.name}'. You can add regexes to it below."
                                    );
                                }
                            }
                            else
                            {
                                Utils.EditorUi.Info(
                                    "Invalid Folder",
                                    "The selected folder must be within the project's 'Assets' directory."
                                );
                            }
                        }
                    }

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Analysis & Actions", EditorStyles.boldLabel);

                    if (GUILayout.Button($"Scan Folders for '{config.name}'"))
                    {
                        ScanFoldersForConfig(config);
                    }

                    if (
                        _scanResultsCache.TryGetValue(config, out ScanResult result)
                        && result.hasScanned
                    )
                    {
                        EditorGUILayout.LabelField(
                            $"Current manually added sprites: {CountValidSprites(config.spritesToPack)}"
                        );
                        EditorGUILayout.LabelField(
                            "Sprites found by scan (not yet added/removed):"
                        );
                        using EditorGUI.IndentLevelScope nextIndentScope = new();

                        if (0 < result.spritesToAdd.Count)
                        {
                            EditorGUILayout.LabelField(
                                $"To Add: {result.spritesToAdd.Count} sprites."
                            );
                            if (
                                GUILayout.Button(
                                    $"Add {result.spritesToAdd.Count} Sprites to '{config.name}' List"
                                )
                            )
                            {
                                AddScannedSprites(config, result);
                            }
                        }
                        else
                        {
                            EditorGUILayout.LabelField(
                                "To Add: 0 sprites.",
                                EditorStyles.miniLabel
                            );
                        }

                        if (0 < result.spritesToRemove.Count)
                        {
                            EditorGUILayout.LabelField(
                                $"To Remove: {result.spritesToRemove.Count} sprites (currently in list but not found by scan)."
                            );
                            if (
                                GUILayout.Button(
                                    $"Remove {result.spritesToRemove.Count} Sprites from '{config.name}' List"
                                )
                            )
                            {
                                RemoveUnfoundSprites(config, result);
                            }
                            if (
                                GUILayout.Button(
                                    $"Sync List To Scan Result ({result.spritesToAdd.Count} add, {result.spritesToRemove.Count} remove)"
                                )
                            )
                            {
                                SyncListToScanResult(config, result);
                            }
                        }
                        else
                        {
                            EditorGUILayout.LabelField(
                                "To Remove: 0 sprites.",
                                EditorStyles.miniLabel
                            );
                        }
                    }
                    else
                    {
                        EditorGUILayout.HelpBox(
                            "Scan to see potential changes from folder sources.",
                            MessageType.None
                        );
                    }

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Source Sprite Utilities", EditorStyles.boldLabel);

                    int validSpriteCount = CountValidSprites(config.spritesToPack);
                    EditorGUI.BeginDisabledGroup(validSpriteCount == 0);
                    if (
                        GUILayout.Button(
                            $"Force Uncompressed for {validSpriteCount} Source Sprites in '{config.name}'"
                        )
                        && Utils.EditorUi.Confirm(
                            "Force Uncompressed Source Sprites",
                            $"This will modify the import settings of {validSpriteCount} source sprites currently in the '{config.name}' list.\n\n"
                                + "- Crunch compression will be disabled.\n"
                                + "- Texture format for the 'Default' platform will be set to uncompressed (RGBA32 or RGB24).\n\n"
                                + "This action modifies source asset import settings and may require re-packing atlases. Are you sure?",
                            "Yes, Modify Source Sprites",
                            "Cancel",
                            defaultWhenSuppressed: true
                        )
                    )
                    {
                        ForceUncompressedSourceSprites(config);
                    }
                    EditorGUI.EndDisabledGroup();

                    EditorGUILayout.Space();
                    if (
                        GUILayout.Button(
                            $"Generate/Update '{config.outputSpriteAtlasName}.spriteatlas' ONLY"
                        )
                        && Utils.EditorUi.Confirm(
                            $"Generate Atlas: {config.name}",
                            $"This will create or update '{config.outputSpriteAtlasName}.spriteatlas'. Continue?",
                            "Yes",
                            "No",
                            defaultWhenSuppressed: true
                        )
                    )
                    {
                        GenerateSingleAtlas(config);
                    }
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }
            EditorGUILayout.EndScrollView();
        }

        private void CreateNewScriptableSpriteAtlas()
        {
            DirectoryHelper.EnsureDirectoryExists(NewAtlasConfigDirectory);
            ScriptableSpriteAtlas newAtlasConfig = CreateInstance<ScriptableSpriteAtlas>();
            string path = AssetDatabase.GenerateUniqueAssetPath(
                Path.Combine(NewAtlasConfigDirectory, "NewScriptableSpriteAtlas.asset")
            );

            AssetDatabaseBatchHelper.EnsureAssetParentFolder(path);
            AssetDatabase.CreateAsset(newAtlasConfig, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.FocusProjectWindow();
            Selection.activeObject = newAtlasConfig;

            this.Log($"Created new ScriptableSpriteAtlas at: {path}");
            LoadAtlasConfigs();
            Repaint();
        }

        private void ScanFoldersForConfig(ScriptableSpriteAtlas config)
        {
            if (config == null)
            {
                return;
            }
            ScanResult result = new ScanResult();
            result.hasScanned = ScriptableSpriteAtlasGenerator.Scan(
                config,
                result.spritesToAdd,
                result.spritesToRemove
            );
            _scanResultsCache[config] = result;
            Repaint();
        }

        private void AddScannedSprites(ScriptableSpriteAtlas config, ScanResult result)
        {
            if (!TryRefreshScanResult(config, result) || result.spritesToAdd.Count <= 0)
            {
                return;
            }

            using SerializedObject so = new(config);
            SerializedProperty spritesListProp = so.FindProperty(
                nameof(ScriptableSpriteAtlas.spritesToPack)
            );

            Undo.RecordObject(config, "Add Scanned Sprites to Atlas Config");

            int addedCount = 0;
            foreach (Sprite sprite in result.spritesToAdd)
            {
                bool alreadyExists = false;
                for (int i = 0; i < spritesListProp.arraySize; ++i)
                {
                    if (spritesListProp.GetArrayElementAtIndex(i).objectReferenceValue == sprite)
                    {
                        alreadyExists = true;
                        break;
                    }
                }
                if (!alreadyExists)
                {
                    SerializedProperty newElement = spritesListProp.AppendArrayElement();
                    newElement.objectReferenceValue = sprite;
                    addedCount++;
                }
            }

            if (0 < addedCount)
            {
                so.ApplyModifiedProperties();
                config.spritesToPack.SortByName();
                EditorUtility.SetDirty(config);
                this.Log($"'{config.name}': Added {addedCount} sprites.");
            }
            else
            {
                this.Log(
                    $"'{config.name}': No new sprites to add (all found sprites might already be in the list)."
                );
            }

            result.spritesToAdd.Clear();
            ScanFoldersForConfig(config);
            Repaint();
        }

        private void RemoveUnfoundSprites(ScriptableSpriteAtlas config, ScanResult result)
        {
            if (!TryRefreshScanResult(config, result) || result.spritesToRemove.Count <= 0)
            {
                return;
            }

            using SerializedObject so = new(config);
            SerializedProperty spritesListProp = so.FindProperty(
                nameof(ScriptableSpriteAtlas.spritesToPack)
            );

            Undo.RecordObject(config, "Remove Unfound Sprites from Atlas Config");

            int countRemoved = 0;
            List<Sprite> spritesActuallyToRemove = new(result.spritesToRemove);

            for (int i = spritesListProp.arraySize - 1; 0 <= i; --i)
            {
                SerializedProperty element = spritesListProp.GetArrayElementAtIndex(i);
                if (
                    element.objectReferenceValue != null
                    && spritesActuallyToRemove.Contains(element.objectReferenceValue as Sprite)
                )
                {
                    element.objectReferenceValue = null;
                    spritesListProp.DeleteArrayElementAtIndex(i);
                    countRemoved++;
                }
            }

            if (0 < countRemoved)
            {
                so.ApplyModifiedProperties();
                this.Log(
                    $"'{config.name}': Removed {countRemoved} sprites that were no longer found by scan."
                );
            }
            result.spritesToRemove.Clear();
            ScanFoldersForConfig(config);
            Repaint();
        }

        private void GenerateSingleAtlas(
            ScriptableSpriteAtlas config,
            bool refreshAssetsImmediately = true
        )
        {
            if (refreshAssetsImmediately)
            {
                ScriptableSpriteAtlasGenerator.Generate(config);
            }
            else
            {
                ScriptableSpriteAtlasGenerator.GenerateWithoutRefresh(config);
            }
        }

        private bool TryRefreshScanResult(ScriptableSpriteAtlas config, ScanResult result)
        {
            if (config == null || result == null || !result.hasScanned)
            {
                return false;
            }
            result.hasScanned = ScriptableSpriteAtlasGenerator.Scan(
                config,
                result.spritesToAdd,
                result.spritesToRemove
            );
            if (!result.hasScanned)
            {
                Repaint();
            }
            return result.hasScanned;
        }

        internal sealed class ScanResult
        {
            public List<Sprite> spritesToAdd = new();
            public List<Sprite> spritesToRemove = new();
            public bool hasScanned;
        }

        private readonly struct AtlasConfigSortEntry
        {
            internal ScriptableSpriteAtlas Config { get; }

            internal int OriginalIndex { get; }

            internal AtlasConfigSortEntry(ScriptableSpriteAtlas config, int originalIndex)
            {
                Config = config;
                OriginalIndex = originalIndex;
            }
        }
    }
#endif
}
