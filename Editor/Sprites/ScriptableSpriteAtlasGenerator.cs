// MIT License - Copyright (c) 2026 wallstop
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
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    /// <summary>Generates and checks sprite atlases from configuration assets.</summary>
    public static class ScriptableSpriteAtlasGenerator
    {
        /// <summary>Finds sprites to add to and remove from a configuration.</summary>
        public static bool Scan(
            ScriptableSpriteAtlas config,
            List<Sprite> spritesToAdd,
            List<Sprite> spritesToRemove
        )
        {
            if (
                config == null
                || spritesToAdd == null
                || spritesToRemove == null
                || ReferenceEquals(spritesToAdd, spritesToRemove)
            )
            {
                return false;
            }
            spritesToAdd.Clear();
            spritesToRemove.Clear();
            if (config.sourceFolderEntries == null || config.sourceFolderEntries.Count == 0)
            {
                return true;
            }
            using PooledResource<HashSet<Sprite>> foundLease = Buffers<Sprite>.HashSet.Get(
                out HashSet<Sprite> found
            );
            foreach (SourceFolderEntry entry in config.sourceFolderEntries)
            {
                if (!ProcessSourceFolderEntry(config, entry, found))
                {
                    return false;
                }
            }
            using PooledResource<HashSet<Sprite>> configuredLease = Buffers<Sprite>.HashSet.Get(
                out HashSet<Sprite> configured
            );
            if (config.spritesToPack != null)
            {
                foreach (Sprite sprite in config.spritesToPack)
                {
                    if (sprite != null)
                    {
                        configured.Add(sprite);
                        if (!found.Contains(sprite))
                        {
                            spritesToRemove.Add(sprite);
                        }
                    }
                }
            }
            foreach (Sprite sprite in found)
            {
                if (sprite != null && !configured.Contains(sprite))
                {
                    spritesToAdd.Add(sprite);
                }
            }
            spritesToAdd.SortByName();
            spritesToRemove.SortByName();
            return true;
        }

        /// <summary>Applies a scan result to a configuration asset.</summary>
        public static bool Synchronize(
            ScriptableSpriteAtlas config,
            IReadOnlyList<Sprite> spritesToAdd,
            IReadOnlyList<Sprite> spritesToRemove,
            bool removeUnmatchedSprites = false
        )
        {
            if (
                config == null
                || spritesToAdd == null
                || spritesToRemove == null
                || config.spritesToPack == null
            )
            {
                return false;
            }
            using PooledResource<HashSet<Sprite>> spritesLease = Buffers<Sprite>.HashSet.Get(
                out HashSet<Sprite> sprites
            );
            foreach (Sprite sprite in config.spritesToPack)
            {
                if (sprite != null)
                {
                    sprites.Add(sprite);
                }
            }
            foreach (Sprite sprite in spritesToAdd)
            {
                if (sprite != null)
                {
                    sprites.Add(sprite);
                }
            }
            if (removeUnmatchedSprites)
            {
                foreach (Sprite sprite in spritesToRemove)
                {
                    if (sprite != null)
                    {
                        sprites.Remove(sprite);
                    }
                }
            }
            Undo.RecordObject(config, "Synchronize Sprite Atlas Config");
            config.spritesToPack.Clear();
            config.spritesToPack.AddRange(sprites);
            config.spritesToPack.SortByName();
            EditorUtility.SetDirty(config);
            return true;
        }

        /// <summary>Generates one atlas and reports whether it changed.</summary>
        public static bool Generate(ScriptableSpriteAtlas config)
        {
            if (!IsValidForGeneration(config) || IsBlockedByOtherAsset(config))
            {
                return false;
            }

            bool normalized = RemoveNullSprites(config);
            bool changed = GenerateCore(config);
            if (changed || normalized)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            return changed;
        }

        /// <summary>Generates all configured atlases and returns the number changed, or -1 if a config failed.</summary>
        public static int GenerateAll()
        {
            return TryGenerateAll(out int changed) ? changed : -1;
        }

        /// <summary>Generates configured atlases and reports whether every configuration succeeded.</summary>
        public static bool TryGenerateAll(out int changed)
        {
            string[] guids = AssetDatabase.FindAssets("t:ScriptableSpriteAtlas");
            changed = 0;
            bool normalized = false;
            bool succeeded = true;
            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
            {
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }
                    ScriptableSpriteAtlas config =
                        AssetDatabase.LoadAssetAtPath<ScriptableSpriteAtlas>(path);
                    if (!IsValidForGeneration(config) || IsBlockedByOtherAsset(config))
                    {
                        succeeded = false;
                        continue;
                    }
                    if (GenerateCore(config))
                    {
                        ++changed;
                    }
                    normalized |= RemoveNullSprites(config);
                }
            }
            if (0 < changed || normalized)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            return succeeded;
        }

        /// <summary>Generates and packs all configured atlases in batch mode.</summary>
        public static void GenerateAndPackAllForBatch()
        {
            if (!TryGenerateAll(out _))
            {
                Debug.LogError("Sprite atlas generation failed for one or more configurations.");
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }
                return;
            }
            PackAll(EditorUserBuildSettings.activeBuildTarget);
        }

        /// <summary>Packs project atlases for the given build target.</summary>
        public static void PackAll(BuildTarget target)
        {
            SpriteAtlasUtility.PackAllAtlases(target);
            AssetDatabase.Refresh();
        }

        /// <summary>Checks an atlas against its configuration without changing either asset.</summary>
        public static bool TryFindDrift(ScriptableSpriteAtlas config, List<string> differences)
        {
            if (differences == null)
            {
                return false;
            }
            differences.Clear();
            if (!IsValid(config))
            {
                differences.Add("Configuration or output path is missing.");
                return false;
            }
            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(config.FullOutputPath);
            if (atlas == null)
            {
                differences.Add(
                    IsOutputOccupied(config.FullOutputPath)
                        ? "Output path is occupied by another asset."
                        : "Atlas is missing."
                );
                return true;
            }
            Object[] packables = atlas.GetPackables();
            int expectedCount = 0;
            if (config.spritesToPack != null)
            {
                foreach (Sprite sprite in config.spritesToPack)
                {
                    if (sprite != null)
                    {
                        if (
                            packables.Length <= expectedCount
                            || !SamePackable(packables[expectedCount], sprite)
                        )
                        {
                            differences.Add("Packables differ.");
                            break;
                        }
                        ++expectedCount;
                    }
                }
            }
            if (expectedCount != packables.Length && !differences.Contains("Packables differ."))
            {
                differences.Add("Packables differ.");
            }
            SpriteAtlasPackingSettings packing = atlas.GetPackingSettings();
            if (
                packing.enableRotation != config.enableRotation
                || packing.padding != config.padding
                || packing.enableTightPacking != config.enableTightPacking
                || packing.enableAlphaDilation != config.enableAlphaDilation
            )
            {
                differences.Add("Packing settings differ.");
            }
            if (atlas.GetTextureSettings().readable != config.readWriteEnabled)
            {
                differences.Add("Texture settings differ.");
            }
            ComparePlatform(
                atlas,
                TexturePlatformNameHelper.DefaultPlatformName,
                true,
                config.maxTextureSize,
                config.compression,
                config.useCrunchCompression,
                config.crunchCompressionLevel,
                differences
            );
            ComparePlatform(
                atlas,
                "Standalone",
                config.overrideStandalone,
                config.standaloneMaxTextureSize,
                config.standaloneCompression,
                config.standaloneUseCrunchCompression,
                config.standaloneCrunchCompressionLevel,
                differences
            );
            ComparePlatform(
                atlas,
                "iPhone",
                config.overrideIPhone,
                config.iPhoneMaxTextureSize,
                config.iPhoneCompression,
                config.iPhoneUseCrunchCompression,
                config.iPhoneCrunchCompressionLevel,
                differences
            );
            ComparePlatform(
                atlas,
                "Android",
                config.overrideAndroid,
                config.androidMaxTextureSize,
                config.androidCompression,
                config.androidUseCrunchCompression,
                config.androidCrunchCompressionLevel,
                differences
            );
            return true;
        }

        internal static bool GenerateWithoutRefresh(ScriptableSpriteAtlas config)
        {
            if (!IsValidForGeneration(config) || IsBlockedByOtherAsset(config))
            {
                return false;
            }
            bool normalized = RemoveNullSprites(config);
            return GenerateCore(config) || normalized;
        }

        private static void AppendNonEmptyStrings(
            IReadOnlyList<string> source,
            List<string> destination
        )
        {
            if (source == null || destination == null)
            {
                return;
            }

            for (int i = 0; i < source.Count; ++i)
            {
                string value = source[i];
                if (!string.IsNullOrWhiteSpace(value))
                {
                    destination.Add(value);
                }
            }
        }

        private static void AppendSanitizedPrefixes(
            IReadOnlyList<string> source,
            List<string> destination
        )
        {
            if (source == null || destination == null)
            {
                return;
            }

            for (int i = 0; i < source.Count; ++i)
            {
                string prefix = source[i];
                if (string.IsNullOrWhiteSpace(prefix))
                {
                    continue;
                }

                destination.Add(prefix.SanitizePath());
            }
        }

        private static bool MatchesLabelRule(
            IReadOnlyList<string> configuredLabels,
            LabelSelectionMode selectionMode,
            IReadOnlyList<string> assetLabels
        )
        {
            if (configuredLabels == null || configuredLabels.Count == 0)
            {
                return true;
            }

            if (assetLabels == null || assetLabels.Count == 0)
            {
                return false;
            }

            switch (selectionMode)
            {
                case LabelSelectionMode.All:
                {
                    for (int i = 0; i < configuredLabels.Count; ++i)
                    {
                        if (!AssetLabelsContain(assetLabels, configuredLabels[i]))
                        {
                            return false;
                        }
                    }
                    return true;
                }
                case LabelSelectionMode.AnyOf:
                {
                    for (int i = 0; i < configuredLabels.Count; ++i)
                    {
                        if (AssetLabelsContain(assetLabels, configuredLabels[i]))
                        {
                            return true;
                        }
                    }
                    return false;
                }
                default:
                    return false;
            }
        }

        private static bool AssetLabelsContain(IReadOnlyList<string> assetLabels, string label)
        {
            if (assetLabels == null || assetLabels.Count == 0 || string.IsNullOrWhiteSpace(label))
            {
                return false;
            }

            for (int i = 0; i < assetLabels.Count; ++i)
            {
                if (string.Equals(assetLabels[i], label, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsValidLabelSelectionMode(LabelSelectionMode mode)
        {
            return mode == LabelSelectionMode.All || mode == LabelSelectionMode.AnyOf;
        }

        private static string[] LoadAssetLabels(string assetPath)
        {
            Object mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (mainAsset == null)
            {
                return Array.Empty<string>();
            }

            string[] labels = AssetDatabase.GetLabels(mainAsset);
            return labels ?? Array.Empty<string>();
        }

        private static bool ProcessSourceFolderEntry(
            ScriptableSpriteAtlas config,
            SourceFolderEntry entry,
            HashSet<Sprite> foundSpritesInFolders
        )
        {
            if (entry == null)
            {
                Debug.LogError(
                    $"'{config.name}': Null source folder entry prevents a complete scan.",
                    config
                );
                return false;
            }

            if (
                string.IsNullOrWhiteSpace(entry.folderPath)
                || !AssetDatabase.IsValidFolder(entry.folderPath)
            )
            {
                Debug.LogError(
                    $"'{config.name}': Invalid or empty folder path '{entry.folderPath}' prevents a complete scan.",
                    config
                );
                return false;
            }

            bool includeRegexFilter = entry.selectionMode.HasFlagNoAlloc(SpriteSelectionMode.Regex);
            bool includeLabelFilter =
                entry.selectionMode.HasFlagNoAlloc(SpriteSelectionMode.Labels)
                && entry.labels is { Count: > 0 };

            if (
                entry.selectionMode != SpriteSelectionMode.Regex
                && entry.selectionMode != SpriteSelectionMode.Labels
                && entry.selectionMode != (SpriteSelectionMode.Regex | SpriteSelectionMode.Labels)
            )
            {
                Debug.LogError(
                    $"'{config.name}', Folder '{entry.folderPath}': Invalid sprite selection mode.",
                    config
                );
                return false;
            }
            if (includeLabelFilter && !IsValidLabelSelectionMode(entry.labelSelectionMode))
            {
                Debug.LogError(
                    $"'{config.name}', Folder '{entry.folderPath}': Invalid label selection mode.",
                    config
                );
                return false;
            }
            if (
                entry.excludeLabels is { Count: > 0 }
                && !IsValidLabelSelectionMode(entry.excludeLabelSelectionMode)
            )
            {
                Debug.LogError(
                    $"'{config.name}', Folder '{entry.folderPath}': Invalid exclusion label selection mode.",
                    config
                );
                return false;
            }
            if (
                includeRegexFilter
                && includeLabelFilter
                && entry.regexAndTagLogic != SpriteSelectionBooleanLogic.And
                && entry.regexAndTagLogic != SpriteSelectionBooleanLogic.Or
            )
            {
                Debug.LogError(
                    $"'{config.name}', Folder '{entry.folderPath}': Invalid regex and label selection logic.",
                    config
                );
                return false;
            }

            List<string> includeLabels = null;
            List<string> excludeLabels = null;
            List<string> excludePrefixes = null;
            List<Regex> compiledRegexes = null;
            List<Regex> compiledExcludeRegexes = null;

            PooledResource<List<string>> includeLabelsLease = default;
            PooledResource<List<string>> excludeLabelsLease = default;
            PooledResource<List<string>> excludePrefixesLease = default;
            PooledResource<List<Regex>> compiledRegexesLease = default;
            PooledResource<List<Regex>> compiledExcludeRegexesLease = default;

            using (
                PooledResource<List<string>> guidListLease = Buffers<string>.List.Get(
                    out List<string> guidList
                )
            )
            {
                try
                {
                    if (includeLabelFilter)
                    {
                        includeLabelsLease = Buffers<string>.List.Get(out includeLabels);
                        AppendNonEmptyStrings(entry.labels, includeLabels);
                        if (includeLabels.Count == 0)
                        {
                            includeLabelFilter = false;
                        }
                        else if (!IsValidLabelSelectionMode(entry.labelSelectionMode))
                        {
                            Debug.LogError(
                                $"'{config.name}', Folder '{entry.folderPath}': Invalid LabelSelectionMode value '{entry.labelSelectionMode}'. Skipping label filtering for this entry."
                            );
                            includeLabelFilter = false;
                        }
                    }

                    bool hasExcludeLabels = entry.excludeLabels is { Count: > 0 };
                    if (hasExcludeLabels)
                    {
                        excludeLabelsLease = Buffers<string>.List.Get(out excludeLabels);
                        AppendNonEmptyStrings(entry.excludeLabels, excludeLabels);
                        if (excludeLabels.Count == 0)
                        {
                            hasExcludeLabels = false;
                        }
                        else if (!IsValidLabelSelectionMode(entry.excludeLabelSelectionMode))
                        {
                            Debug.LogError(
                                $"'{config.name}', Folder '{entry.folderPath}': Invalid LabelSelectionMode value '{entry.excludeLabelSelectionMode}'. Skipping exclude label filtering for this entry."
                            );
                            hasExcludeLabels = false;
                        }
                    }

                    bool hasExcludePrefixes = entry.excludePathPrefixes is { Count: > 0 };
                    if (hasExcludePrefixes)
                    {
                        excludePrefixesLease = Buffers<string>.List.Get(out excludePrefixes);
                        AppendSanitizedPrefixes(entry.excludePathPrefixes, excludePrefixes);
                        if (excludePrefixes.Count == 0)
                        {
                            hasExcludePrefixes = false;
                        }
                    }

                    bool searchedByLabels =
                        includeLabelFilter
                        && TryFindLabelFilteredAssets(config, entry, includeLabels, guidList);

                    if (!searchedByLabels)
                    {
                        string[] defaultGuids = AssetDatabase.FindAssets(
                            "t:Texture2D",
                            new[] { entry.folderPath }
                        );
                        if (defaultGuids != null && 0 < defaultGuids.Length)
                        {
                            guidList.AddRange(defaultGuids);
                        }
                    }

                    if (includeRegexFilter && entry.regexes is { Count: > 0 })
                    {
                        compiledRegexesLease = Buffers<Regex>.List.Get(out compiledRegexes);
                        if (
                            !CompileRegexPatterns(
                                config,
                                entry,
                                entry.regexes,
                                compiledRegexes,
                                "Regex"
                            )
                        )
                        {
                            return false;
                        }
                    }

                    bool hasExcludeRegexes = entry.excludeRegexes is { Count: > 0 };
                    if (hasExcludeRegexes)
                    {
                        compiledExcludeRegexesLease = Buffers<Regex>.List.Get(
                            out compiledExcludeRegexes
                        );
                        if (
                            !CompileRegexPatterns(
                                config,
                                entry,
                                entry.excludeRegexes,
                                compiledExcludeRegexes,
                                "Exclude Regex"
                            )
                        )
                        {
                            return false;
                        }
                        if (compiledExcludeRegexes.Count == 0)
                        {
                            hasExcludeRegexes = false;
                        }
                    }

                    if (guidList.Count == 0)
                    {
                        return true;
                    }

                    bool needsExcludeLabels =
                        hasExcludeLabels && excludeLabels != null && 0 < excludeLabels.Count;
                    bool needsLabels = includeLabelFilter || needsExcludeLabels;

                    foreach (string guid in guidList)
                    {
                        string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                        if (string.IsNullOrWhiteSpace(assetPath))
                        {
                            continue;
                        }

                        string fileName = Path.GetFileName(assetPath);
                        bool regexMatch = true;
                        if (
                            compiledRegexes != null
                            && 0 < compiledRegexes.Count
                            && !string.IsNullOrEmpty(fileName)
                        )
                        {
                            foreach (Regex rx in compiledRegexes)
                            {
                                if (!rx.IsMatch(fileName))
                                {
                                    regexMatch = false;
                                    break;
                                }
                            }
                        }

                        string[] assetLabels = needsLabels
                            ? LoadAssetLabels(assetPath)
                            : Array.Empty<string>();
                        bool labelMatch = true;
                        if (includeLabelFilter)
                        {
                            labelMatch = MatchesLabelRule(
                                includeLabels,
                                entry.labelSelectionMode,
                                assetLabels
                            );
                        }

                        bool passesFilters;
                        if (includeRegexFilter && includeLabelFilter)
                        {
                            switch (entry.regexAndTagLogic)
                            {
                                case SpriteSelectionBooleanLogic.And:
                                    passesFilters = regexMatch && labelMatch;
                                    break;
                                case SpriteSelectionBooleanLogic.Or:
                                    passesFilters = regexMatch || labelMatch;
                                    break;
                                default:
                                    Debug.LogError(
                                        $"'{config.name}', Folder '{entry.folderPath}': Invalid SpriteSelectionBooleanLogic value '{entry.regexAndTagLogic}'. Defaulting to AND logic."
                                    );
                                    passesFilters = regexMatch && labelMatch;
                                    break;
                            }
                        }
                        else if (includeRegexFilter)
                        {
                            passesFilters = regexMatch;
                        }
                        else if (includeLabelFilter)
                        {
                            passesFilters = labelMatch;
                        }
                        else
                        {
                            passesFilters = true;
                        }

                        if (!passesFilters)
                        {
                            continue;
                        }

                        bool excluded = false;
                        if (!excluded && hasExcludePrefixes && excludePrefixes != null)
                        {
                            string sanitizedAssetPath = assetPath.SanitizePath();
                            foreach (string prefix in excludePrefixes)
                            {
                                if (
                                    sanitizedAssetPath.StartsWith(
                                        prefix,
                                        StringComparison.OrdinalIgnoreCase
                                    )
                                )
                                {
                                    excluded = true;
                                    break;
                                }
                            }
                        }

                        if (
                            !excluded
                            && hasExcludeRegexes
                            && compiledExcludeRegexes != null
                            && !string.IsNullOrEmpty(fileName)
                        )
                        {
                            foreach (Regex rx in compiledExcludeRegexes)
                            {
                                if (rx.IsMatch(fileName))
                                {
                                    excluded = true;
                                    break;
                                }
                            }
                        }

                        if (!excluded && needsExcludeLabels)
                        {
                            excluded = MatchesLabelRule(
                                excludeLabels,
                                entry.excludeLabelSelectionMode,
                                assetLabels
                            );
                        }

                        if (excluded)
                        {
                            continue;
                        }

                        Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                        if (assets == null || assets.Length == 0)
                        {
                            continue;
                        }

                        foreach (Object asset in assets)
                        {
                            if (asset is Sprite spriteAsset && spriteAsset != null)
                            {
                                foundSpritesInFolders.Add(spriteAsset);
                            }
                        }
                    }
                }
                finally
                {
                    compiledRegexesLease.Dispose();
                    compiledExcludeRegexesLease.Dispose();
                    includeLabelsLease.Dispose();
                    excludeLabelsLease.Dispose();
                    excludePrefixesLease.Dispose();
                }
            }
            return true;
        }

        private static bool TryFindLabelFilteredAssets(
            ScriptableSpriteAtlas config,
            SourceFolderEntry entry,
            IReadOnlyList<string> includeLabels,
            List<string> guidList
        )
        {
            if (includeLabels == null || includeLabels.Count == 0)
            {
                return false;
            }

            if (!IsValidLabelSelectionMode(entry.labelSelectionMode))
            {
                Debug.LogError(
                    $"'{config.name}', Folder '{entry.folderPath}': Invalid LabelSelectionMode value '{entry.labelSelectionMode}'. Skipping label pre-filter."
                );
                return false;
            }

            switch (entry.labelSelectionMode)
            {
                case LabelSelectionMode.All:
                {
                    string query = "t:Texture2D";
                    for (int i = 0; i < includeLabels.Count; ++i)
                    {
                        string label = includeLabels[i];
                        if (!string.IsNullOrWhiteSpace(label))
                        {
                            query += $" l:{label}";
                        }
                    }

                    string[] guids = AssetDatabase.FindAssets(query, new[] { entry.folderPath });
                    if (guids != null && 0 < guids.Length)
                    {
                        guidList.AddRange(guids);
                    }
                    return true;
                }
                case LabelSelectionMode.AnyOf:
                {
                    using (
                        PooledResource<HashSet<string>> setLease = Buffers<string>.HashSet.Get(
                            out HashSet<string> set
                        )
                    )
                    {
                        for (int i = 0; i < includeLabels.Count; ++i)
                        {
                            string label = includeLabels[i];
                            if (string.IsNullOrWhiteSpace(label))
                            {
                                continue;
                            }

                            string query = $"t:Texture2D l:{label}";
                            string[] guids = AssetDatabase.FindAssets(
                                query,
                                new[] { entry.folderPath }
                            );
                            if (guids == null || guids.Length == 0)
                            {
                                continue;
                            }

                            foreach (string guidsElement in guids)
                            {
                                set.Add(guidsElement);
                            }
                        }

                        if (0 < set.Count)
                        {
                            guidList.AddRange(set);
                        }
                    }
                    return true;
                }
                default:
                {
                    return false;
                }
            }
        }

        private static bool CompileRegexPatterns(
            ScriptableSpriteAtlas config,
            SourceFolderEntry entry,
            IReadOnlyList<string> patterns,
            List<Regex> destination,
            string description
        )
        {
            if (patterns == null || destination == null)
            {
                return false;
            }

            for (int i = 0; i < patterns.Count; ++i)
            {
                string pattern = patterns[i];
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    continue;
                }

                try
                {
                    destination.Add(
                        new Regex(
                            pattern,
                            RegexOptions.IgnoreCase
                                | RegexOptions.CultureInvariant
                                | RegexOptions.Compiled
                        )
                    );
                }
                catch (ArgumentException e)
                {
                    Debug.LogError(
                        $"'{config.name}', Folder '{entry.folderPath}': Invalid {description} pattern '{pattern}'. This pattern will be ignored.",
                        config
                    );
                    Debug.LogException(e, config);
                    return false;
                }
            }
            return true;
        }

        private static bool IsValid(ScriptableSpriteAtlas config)
        {
            if (config == null)
            {
                return false;
            }
            string path = config.FullOutputPath;
            return !string.IsNullOrWhiteSpace(path)
                && path.StartsWith("Assets/", StringComparison.Ordinal)
                && !path.Contains("/../")
                && !path.Contains("/./");
        }

        private static bool IsValidForGeneration(ScriptableSpriteAtlas config)
        {
            if (IsValid(config))
            {
                return true;
            }

            Debug.LogError(
                config == null
                    ? "Sprite atlas configuration is missing."
                    : $"'{config.name}': Output atlas path '{config.FullOutputPath}' must be under Assets/ and cannot be empty or contain relative segments.",
                config
            );
            return false;
        }

        private static bool IsOutputOccupied(string path)
        {
            return File.Exists(Path.Combine(Path.GetDirectoryName(Application.dataPath), path));
        }

        private static bool IsBlockedByOtherAsset(ScriptableSpriteAtlas config)
        {
            string path = config.FullOutputPath;
            if (AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path) != null || !IsOutputOccupied(path))
            {
                return false;
            }
            Debug.LogError(
                $"'{config.name}': Output path '{path}' is occupied by another asset.",
                config
            );
            return true;
        }

        private static bool SamePackable(Object actual, Sprite expected)
        {
            if (actual == expected)
            {
                return true;
            }
            if (actual == null || expected == null)
            {
                return false;
            }
            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    actual,
                    out string actualGuid,
                    out long actualLocalId
                )
                && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    expected,
                    out string expectedGuid,
                    out long expectedLocalId
                )
                && string.Equals(actualGuid, expectedGuid, StringComparison.Ordinal)
                && actualLocalId == expectedLocalId;
        }

        private static bool RemoveNullSprites(ScriptableSpriteAtlas config)
        {
            if (config.spritesToPack == null)
            {
                return false;
            }
            bool hasMissingSprite = false;
            foreach (Sprite sprite in config.spritesToPack)
            {
                if (sprite == null)
                {
                    hasMissingSprite = true;
                    break;
                }
            }
            if (!hasMissingSprite)
            {
                return false;
            }
            Undo.RecordObject(config, "Remove Missing Sprite Atlas Entries");
            config.spritesToPack.RemoveAll(sprite => sprite == null);
            EditorUtility.SetDirty(config);
            return true;
        }

        private static bool GenerateCore(ScriptableSpriteAtlas config)
        {
            List<string> differences = new List<string>();
            if (TryFindDrift(config, differences) && differences.Count == 0)
            {
                return false;
            }
            string outputPath = config.FullOutputPath;
            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(outputPath);
            bool created = atlas == null;
            if (created)
            {
                if (IsOutputOccupied(outputPath))
                {
                    Debug.LogError(
                        $"'{config.name}': Output path '{outputPath}' is occupied by another asset.",
                        config
                    );
                    return false;
                }
                atlas = new SpriteAtlas();
            }
            else
            {
                Undo.RecordObject(atlas, "Update Sprite Atlas");
                atlas.Remove(atlas.GetPackables());
            }
            SpriteAtlasPackingSettings packing = atlas.GetPackingSettings();
            packing.enableRotation = config.enableRotation;
            packing.padding = config.padding;
            packing.enableTightPacking = config.enableTightPacking;
            packing.enableAlphaDilation = config.enableAlphaDilation;
            atlas.SetPackingSettings(packing);
            SpriteAtlasTextureSettings texture = atlas.GetTextureSettings();
            texture.readable = config.readWriteEnabled;
            atlas.SetTextureSettings(texture);
            ApplyPlatform(
                atlas,
                TexturePlatformNameHelper.DefaultPlatformName,
                true,
                config.maxTextureSize,
                config.compression,
                config.useCrunchCompression,
                config.crunchCompressionLevel
            );
            ApplyPlatform(
                atlas,
                "Standalone",
                config.overrideStandalone,
                config.standaloneMaxTextureSize,
                config.standaloneCompression,
                config.standaloneUseCrunchCompression,
                config.standaloneCrunchCompressionLevel
            );
            ApplyPlatform(
                atlas,
                "iPhone",
                config.overrideIPhone,
                config.iPhoneMaxTextureSize,
                config.iPhoneCompression,
                config.iPhoneUseCrunchCompression,
                config.iPhoneCrunchCompressionLevel
            );
            ApplyPlatform(
                atlas,
                "Android",
                config.overrideAndroid,
                config.androidMaxTextureSize,
                config.androidCompression,
                config.androidUseCrunchCompression,
                config.androidCrunchCompressionLevel
            );
            if (config.spritesToPack != null)
            {
                List<Object> sprites = new List<Object>(config.spritesToPack.Count);
                foreach (Sprite sprite in config.spritesToPack)
                {
                    if (sprite != null)
                    {
                        sprites.Add(sprite);
                    }
                }
                if (0 < sprites.Count)
                {
                    atlas.Add(sprites.ToArray());
                }
            }
            if (created)
            {
                AssetDatabaseBatchHelper.EnsureAssetParentFolder(outputPath);
                AssetDatabase.CreateAsset(atlas, outputPath);
                if (
                    !string.Equals(
                        AssetDatabase.GetAssetPath(atlas),
                        outputPath,
                        StringComparison.Ordinal
                    )
                )
                {
                    Debug.LogError(
                        $"'{config.name}': Failed to create Sprite Atlas at '{outputPath}'.",
                        config
                    );
                    Object.DestroyImmediate(atlas);
                    return false;
                }
            }
            else
            {
                EditorUtility.SetDirty(atlas);
            }
            return true;
        }

        private static void ApplyPlatform(
            SpriteAtlas atlas,
            string name,
            bool overridden,
            int maxSize,
            TextureImporterCompression compression,
            bool crunched,
            int quality
        )
        {
            TextureImporterPlatformSettings settings = atlas.GetPlatformSettings(name);
            settings.name = name;
            settings.overridden = overridden;
            if (overridden)
            {
                settings.maxTextureSize = maxSize;
                settings.textureCompression = compression;
                settings.crunchedCompression = crunched;
                settings.compressionQuality = Mathf.Clamp(quality, 0, 100);
                settings.format = TextureImporterFormat.Automatic;
            }
            atlas.SetPlatformSettings(settings);
        }

        private static void ComparePlatform(
            SpriteAtlas atlas,
            string name,
            bool overridden,
            int maxSize,
            TextureImporterCompression compression,
            bool crunched,
            int quality,
            List<string> differences
        )
        {
            TextureImporterPlatformSettings settings = atlas.GetPlatformSettings(name);
            if (
                settings.overridden != overridden
                || (
                    overridden
                    && (
                        settings.maxTextureSize != maxSize
                        || settings.textureCompression != compression
                        || settings.crunchedCompression != crunched
                        || settings.compressionQuality != Mathf.Clamp(quality, 0, 100)
                        || settings.format != TextureImporterFormat.Automatic
                    )
                )
            )
            {
                differences.Add(name + " platform settings differ.");
            }
        }
    }
#endif
}
