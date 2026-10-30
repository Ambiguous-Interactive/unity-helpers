// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Programmatic API for applying generic texture importer settings (non-sprite specific)
    /// with support for selective fields and default-platform overrides.
    /// </summary>
    /// <remarks>
    /// Null, empty and whitespace-only platform override names are ignored.
    /// Nonblank names are passed unchanged to Unity.
    /// </remarks>
    public static class TextureSettingsApplierAPI
    {
        /// <summary>Applies texture settings to explicit asset paths and persists changed importers.</summary>
        /// <remarks>
        /// Cancellation retains completed changes. Reimport and asset-database side effects cannot
        /// be fully reversed by Unity Undo; a failure may leave earlier textures changed.
        /// </remarks>
        public static bool TryApplyTextureSettings(
            IReadOnlyList<string> assetPaths,
            in Config config,
            out int changedCount,
            out bool canceled,
            out string error,
            Func<int, int, string, bool> cancelRequested = null,
            TextureImporterSettings buffer = null,
            Action beforeReimport = null
        )
        {
            if (assetPaths == null)
            {
                changedCount = 0;
                canceled = false;
                error = "Asset paths are required.";
                return false;
            }

            int localChangedCount = 0;
            bool localCanceled = false;
            string localError = null;
            using PooledResource<List<TextureImporter>> changedLease =
                Buffers<TextureImporter>.List.Get(out List<TextureImporter> changed);
            try
            {
                using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
                {
                    for (int index = 0; index < assetPaths.Count; ++index)
                    {
                        string path = assetPaths[index].SanitizePath();
                        if (
                            cancelRequested != null
                            && cancelRequested(index, assetPaths.Count, path)
                        )
                        {
                            localCanceled = true;
                            break;
                        }

                        if (
                            TryUpdateTextureSettings(
                                path,
                                in config,
                                out TextureImporter importer,
                                buffer
                            )
                            && importer != null
                        )
                        {
                            changed.Add(importer);
                            ++localChangedCount;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                localError = $"Texture settings apply failed: {exception.Message}";
            }

            try
            {
                beforeReimport?.Invoke();
            }
            catch (Exception exception)
            {
                localError ??= $"Texture settings completion callback failed: {exception.Message}";
            }

            foreach (TextureImporter importer in changed)
            {
                try
                {
                    importer.SaveAndReimport();
                }
                catch (Exception exception)
                {
                    localError ??= $"Texture reimport failed: {exception.Message}";
                }
            }

            if (0 < localChangedCount)
            {
                try
                {
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }
                catch (Exception exception)
                {
                    localError ??= $"Texture settings save failed: {exception.Message}";
                }
            }
            changedCount = localChangedCount;
            canceled = localCanceled;
            error = localError;
            return localError == null;
        }

        public static bool WillTextureSettingsChange(
            string assetPath,
            in Config config,
            TextureImporterSettings buffer = null
        )
        {
            TextureImporter ti = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (ti == null)
            {
                return false;
            }

            bool changed = false;

            if (config.applyReadWriteEnabled)
            {
                bool isReadableChanged = ti.isReadable != config.readWriteEnabled;
                changed = changed || isReadableChanged;
            }
            if (config.applyMipMaps)
            {
                bool mipmapEnabledChanged = ti.mipmapEnabled != config.generateMipMaps;
                changed = changed || mipmapEnabledChanged;
            }
            if (config.applyWrapMode)
            {
                bool wrapModeChanged = ti.wrapMode != config.wrapMode;
                changed = changed || wrapModeChanged;
            }
            if (config.applyFilterMode)
            {
                bool filterModeChanged = ti.filterMode != config.filterMode;
                changed = changed || filterModeChanged;
            }
            if (config.applyCompression)
            {
                bool textureCompressionChanged = ti.textureCompression != config.compression;
                changed = changed || textureCompressionChanged;
            }
            if (config.applyCrunchCompression)
            {
                bool crunchedCompressionChanged =
                    ti.crunchedCompression != config.useCrunchCompression;
                changed = changed || crunchedCompressionChanged;
            }

            buffer ??= new TextureImporterSettings();
            ti.ReadTextureSettings(buffer);

            TextureImporterPlatformSettings ps = ti.GetDefaultPlatformTextureSettings();
            if (config.applyPlatformResizeAlgorithm)
            {
                bool resizeAlgorithmChanged = ps.resizeAlgorithm != config.platformResizeAlgorithm;
                changed = changed || resizeAlgorithmChanged;
            }
            if (config.applyPlatformMaxTextureSize)
            {
                bool maxTextureSizeChanged = ps.maxTextureSize != config.platformMaxTextureSize;
                changed = changed || maxTextureSizeChanged;
            }
            if (config.applyPlatformFormat)
            {
                bool formatChanged = ps.format != config.platformFormat;
                changed = changed || formatChanged;
            }
            if (config.applyPlatformCompression)
            {
                bool textureCompressionChanged =
                    ps.textureCompression != config.platformCompression;
                changed = changed || textureCompressionChanged;
            }
            if (config.applyPlatformCrunchCompression)
            {
                bool crunchedCompressionChanged =
                    ps.crunchedCompression != config.platformUseCrunchCompression;
                changed = changed || crunchedCompressionChanged;
            }

            if (config.platformOverrides != null)
            {
                foreach (PlatformOverride po in config.platformOverrides)
                {
                    if (string.IsNullOrWhiteSpace(po.name))
                    {
                        continue;
                    }

                    TextureImporterPlatformSettings ops = string.Equals(
                        po.name,
                        TexturePlatformNameHelper.DefaultPlatformName,
                        System.StringComparison.Ordinal
                    )
                        ? ti.GetDefaultPlatformTextureSettings()
                        : ti.GetPlatformTextureSettings(po.name);
                    if (po.applyResizeAlgorithm)
                    {
                        bool resizeAlgorithmChanged = ops.resizeAlgorithm != po.resizeAlgorithm;
                        changed = changed || resizeAlgorithmChanged;
                    }

                    if (po.applyMaxTextureSize)
                    {
                        bool maxTextureSizeChanged = ops.maxTextureSize != po.maxTextureSize;
                        changed = changed || maxTextureSizeChanged;
                    }

                    if (po.applyFormat)
                    {
                        bool formatChanged = ops.format != po.format;
                        changed = changed || formatChanged;
                    }

                    if (po.applyCompression)
                    {
                        bool textureCompressionChanged = ops.textureCompression != po.compression;
                        changed = changed || textureCompressionChanged;
                    }

                    if (po.applyCrunchCompression)
                    {
                        bool crunchedCompressionChanged =
                            ops.crunchedCompression != po.useCrunchCompression;
                        changed = changed || crunchedCompressionChanged;
                    }
                }
            }

            return changed;
        }

        public static bool TryUpdateTextureSettings(
            string assetPath,
            in Config config,
            out TextureImporter textureImporter,
            TextureImporterSettings buffer = null
        )
        {
            TextureImporter localTextureImporter =
                AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (localTextureImporter == null)
            {
                textureImporter = null;
                return false;
            }

            bool changed = false;
            bool settingsChanged = false;
            bool undoRecorded = false;

            if (config.applyReadWriteEnabled)
            {
                if (localTextureImporter.isReadable != config.readWriteEnabled)
                {
                    EnsureUndoRecorded();
                    localTextureImporter.isReadable = config.readWriteEnabled;
                    changed = true;
                }
            }
            if (config.applyMipMaps)
            {
                if (localTextureImporter.mipmapEnabled != config.generateMipMaps)
                {
                    EnsureUndoRecorded();
                    localTextureImporter.mipmapEnabled = config.generateMipMaps;
                    changed = true;
                }
            }
            if (config.applyWrapMode)
            {
                if (localTextureImporter.wrapMode != config.wrapMode)
                {
                    EnsureUndoRecorded();
                    localTextureImporter.wrapMode = config.wrapMode;
                    changed = true;
                }
            }
            if (config.applyFilterMode)
            {
                if (localTextureImporter.filterMode != config.filterMode)
                {
                    EnsureUndoRecorded();
                    localTextureImporter.filterMode = config.filterMode;
                    changed = true;
                }
            }
            if (config.applyCompression)
            {
                if (localTextureImporter.textureCompression != config.compression)
                {
                    EnsureUndoRecorded();
                    localTextureImporter.textureCompression = config.compression;
                    changed = true;
                }
            }
            if (config.applyCrunchCompression)
            {
                if (localTextureImporter.crunchedCompression != config.useCrunchCompression)
                {
                    EnsureUndoRecorded();
                    localTextureImporter.crunchedCompression = config.useCrunchCompression;
                    changed = true;
                }
            }

            buffer ??= new TextureImporterSettings();
            localTextureImporter.ReadTextureSettings(buffer);

            bool platformChanged = false;
            TextureImporterPlatformSettings ps =
                localTextureImporter.GetDefaultPlatformTextureSettings();
            if (
                config.applyPlatformResizeAlgorithm
                && ps.resizeAlgorithm != config.platformResizeAlgorithm
            )
            {
                ps.resizeAlgorithm = config.platformResizeAlgorithm;
                platformChanged = true;
            }
            if (
                config.applyPlatformMaxTextureSize
                && ps.maxTextureSize != config.platformMaxTextureSize
            )
            {
                ps.maxTextureSize = config.platformMaxTextureSize;
                platformChanged = true;
            }
            if (config.applyPlatformFormat && ps.format != config.platformFormat)
            {
                ps.format = config.platformFormat;
                platformChanged = true;
            }
            if (
                config.applyPlatformCompression
                && ps.textureCompression != config.platformCompression
            )
            {
                ps.textureCompression = config.platformCompression;
                platformChanged = true;
            }
            if (
                config.applyPlatformCrunchCompression
                && ps.crunchedCompression != config.platformUseCrunchCompression
            )
            {
                ps.crunchedCompression = config.platformUseCrunchCompression;
                platformChanged = true;
            }
            if (platformChanged)
            {
                EnsureUndoRecorded();
                localTextureImporter.SetPlatformTextureSettings(ps);
                changed = true;
            }

            if (config.platformOverrides != null)
            {
                foreach (PlatformOverride po in config.platformOverrides)
                {
                    if (string.IsNullOrWhiteSpace(po.name))
                    {
                        continue;
                    }

                    TextureImporterPlatformSettings ops = string.Equals(
                        po.name,
                        TexturePlatformNameHelper.DefaultPlatformName,
                        System.StringComparison.Ordinal
                    )
                        ? localTextureImporter.GetDefaultPlatformTextureSettings()
                        : localTextureImporter.GetPlatformTextureSettings(po.name);
                    bool any = false;
                    if (po.applyResizeAlgorithm && ops.resizeAlgorithm != po.resizeAlgorithm)
                    {
                        ops.resizeAlgorithm = po.resizeAlgorithm;
                        any = true;
                    }

                    if (po.applyMaxTextureSize && ops.maxTextureSize != po.maxTextureSize)
                    {
                        ops.maxTextureSize = po.maxTextureSize;
                        any = true;
                    }

                    if (po.applyFormat && ops.format != po.format)
                    {
                        ops.format = po.format;
                        any = true;
                    }

                    if (po.applyCompression && ops.textureCompression != po.compression)
                    {
                        ops.textureCompression = po.compression;
                        any = true;
                    }

                    if (
                        po.applyCrunchCompression
                        && ops.crunchedCompression != po.useCrunchCompression
                    )
                    {
                        ops.crunchedCompression = po.useCrunchCompression;
                        any = true;
                    }

                    if (any)
                    {
                        ops.overridden = !string.Equals(
                            po.name,
                            TexturePlatformNameHelper.DefaultPlatformName,
                            System.StringComparison.Ordinal
                        )
                            ? true
                            : ops.overridden;
                        EnsureUndoRecorded();
                        localTextureImporter.SetPlatformTextureSettings(ops);
                        changed = true;
                    }
                }
            }

            // We currently do not mutate buffer-only fields for non-sprite textures.
            if (settingsChanged)
            {
                EnsureUndoRecorded();
                localTextureImporter.SetTextureSettings(buffer);
            }

            textureImporter = localTextureImporter;
            return changed || settingsChanged;

            void EnsureUndoRecorded()
            {
                if (undoRecorded)
                {
                    return;
                }

                Undo.RecordObject(localTextureImporter, "Apply Texture Settings");
                undoRecorded = true;
            }
        }

        [Serializable]
        public struct PlatformOverride : global::System.IEquatable<PlatformOverride>
        {
            public string name; // e.g. "DefaultTexturePlatform", "Standalone", "iPhone", "Android"

            public bool applyResizeAlgorithm;
            public TextureResizeAlgorithm resizeAlgorithm;
            public bool applyMaxTextureSize;
            public int maxTextureSize;
            public bool applyFormat;
            public TextureImporterFormat format;
            public bool applyCompression;
            public TextureImporterCompression compression;
            public bool applyCrunchCompression;
            public bool useCrunchCompression;

            /// <summary>Compares the stored fields with another value.</summary>
            public bool Equals(PlatformOverride other)
            {
                return EqualityComparer<string>.Default.Equals(name, other.name)
                    && applyResizeAlgorithm.Equals(other.applyResizeAlgorithm)
                    && EqualityComparer<TextureResizeAlgorithm>.Default.Equals(
                        resizeAlgorithm,
                        other.resizeAlgorithm
                    )
                    && applyMaxTextureSize.Equals(other.applyMaxTextureSize)
                    && maxTextureSize.Equals(other.maxTextureSize)
                    && applyFormat.Equals(other.applyFormat)
                    && EqualityComparer<TextureImporterFormat>.Default.Equals(format, other.format)
                    && applyCompression.Equals(other.applyCompression)
                    && EqualityComparer<TextureImporterCompression>.Default.Equals(
                        compression,
                        other.compression
                    )
                    && applyCrunchCompression.Equals(other.applyCrunchCompression)
                    && useCrunchCompression.Equals(other.useCrunchCompression);
            }

            /// <inheritdoc />
            public override bool Equals(object obj)
            {
                return obj is PlatformOverride other && Equals(other);
            }

            /// <inheritdoc />
            public override int GetHashCode()
            {
                return global::WallstopStudios.UnityHelpers.Core.Helper.Objects.HashCode(
                    name,
                    applyResizeAlgorithm,
                    resizeAlgorithm,
                    applyMaxTextureSize,
                    maxTextureSize,
                    applyFormat,
                    format,
                    applyCompression,
                    compression,
                    applyCrunchCompression,
                    useCrunchCompression
                );
            }
        }

        [Serializable]
        public struct Config : global::System.IEquatable<Config>
        {
            public bool applyReadWriteEnabled;
            public bool readWriteEnabled;

            public bool applyMipMaps;
            public bool generateMipMaps;

            public bool applyWrapMode;
            public TextureWrapMode wrapMode;
            public bool applyFilterMode;
            public FilterMode filterMode;

            public bool applyCompression;
            public TextureImporterCompression compression;
            public bool applyCrunchCompression;
            public bool useCrunchCompression;

            public bool applyPlatformResizeAlgorithm;
            public TextureResizeAlgorithm platformResizeAlgorithm;
            public bool applyPlatformMaxTextureSize;
            public int platformMaxTextureSize;
            public bool applyPlatformFormat;
            public TextureImporterFormat platformFormat;
            public bool applyPlatformCompression;
            public TextureImporterCompression platformCompression;
            public bool applyPlatformCrunchCompression;
            public bool platformUseCrunchCompression;

            public PlatformOverride[] platformOverrides;

            /// <summary>Compares the stored fields with another value.</summary>
            public bool Equals(Config other)
            {
                return applyReadWriteEnabled.Equals(other.applyReadWriteEnabled)
                    && readWriteEnabled.Equals(other.readWriteEnabled)
                    && applyMipMaps.Equals(other.applyMipMaps)
                    && generateMipMaps.Equals(other.generateMipMaps)
                    && applyWrapMode.Equals(other.applyWrapMode)
                    && EqualityComparer<TextureWrapMode>.Default.Equals(wrapMode, other.wrapMode)
                    && applyFilterMode.Equals(other.applyFilterMode)
                    && EqualityComparer<FilterMode>.Default.Equals(filterMode, other.filterMode)
                    && applyCompression.Equals(other.applyCompression)
                    && EqualityComparer<TextureImporterCompression>.Default.Equals(
                        compression,
                        other.compression
                    )
                    && applyCrunchCompression.Equals(other.applyCrunchCompression)
                    && useCrunchCompression.Equals(other.useCrunchCompression)
                    && applyPlatformResizeAlgorithm.Equals(other.applyPlatformResizeAlgorithm)
                    && EqualityComparer<TextureResizeAlgorithm>.Default.Equals(
                        platformResizeAlgorithm,
                        other.platformResizeAlgorithm
                    )
                    && applyPlatformMaxTextureSize.Equals(other.applyPlatformMaxTextureSize)
                    && platformMaxTextureSize.Equals(other.platformMaxTextureSize)
                    && applyPlatformFormat.Equals(other.applyPlatformFormat)
                    && EqualityComparer<TextureImporterFormat>.Default.Equals(
                        platformFormat,
                        other.platformFormat
                    )
                    && applyPlatformCompression.Equals(other.applyPlatformCompression)
                    && EqualityComparer<TextureImporterCompression>.Default.Equals(
                        platformCompression,
                        other.platformCompression
                    )
                    && applyPlatformCrunchCompression.Equals(other.applyPlatformCrunchCompression)
                    && platformUseCrunchCompression.Equals(other.platformUseCrunchCompression)
                    && EqualityComparer<PlatformOverride[]>.Default.Equals(
                        platformOverrides,
                        other.platformOverrides
                    );
            }

            /// <inheritdoc />
            public override bool Equals(object obj)
            {
                return obj is Config other && Equals(other);
            }

            /// <inheritdoc />
            public override int GetHashCode()
            {
                return global::WallstopStudios.UnityHelpers.Core.Helper.Objects.HashCode(
                    global::WallstopStudios.UnityHelpers.Core.Helper.Objects.HashCode(
                        applyReadWriteEnabled,
                        readWriteEnabled,
                        applyMipMaps,
                        generateMipMaps,
                        applyWrapMode,
                        wrapMode,
                        applyFilterMode,
                        filterMode,
                        applyCompression,
                        compression,
                        applyCrunchCompression,
                        useCrunchCompression,
                        applyPlatformResizeAlgorithm,
                        platformResizeAlgorithm,
                        applyPlatformMaxTextureSize,
                        platformMaxTextureSize,
                        applyPlatformFormat,
                        platformFormat,
                        applyPlatformCompression,
                        platformCompression
                    ),
                    applyPlatformCrunchCompression,
                    platformUseCrunchCompression,
                    platformOverrides
                );
            }
        }
    }
#endif
}
