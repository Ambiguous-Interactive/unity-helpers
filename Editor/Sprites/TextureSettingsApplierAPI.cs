// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Programmatic API for applying generic texture importer settings (non-sprite specific)
    /// with support for selective fields and default-platform overrides.
    /// </summary>
    public static class TextureSettingsApplierAPI
    {
        [Serializable]
        public struct PlatformOverride
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
        }

        [Serializable]
        public struct Config
        {
            // Read/Write
            public bool applyReadWriteEnabled;
            public bool readWriteEnabled;

            // MipMaps
            public bool applyMipMaps;
            public bool generateMipMaps;

            // Sampler
            public bool applyWrapMode;
            public TextureWrapMode wrapMode;
            public bool applyFilterMode;
            public FilterMode filterMode;

            // Importer-level compression convenience (kept for parity with Sprite API)
            public bool applyCompression;
            public TextureImporterCompression compression;
            public bool applyCrunchCompression;
            public bool useCrunchCompression;

            // Default Platform Settings overrides
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

            // Optional set of named per-platform overrides.
            public PlatformOverride[] platformOverrides;
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
                changed |= ti.isReadable != config.readWriteEnabled;
            }
            if (config.applyMipMaps)
            {
                changed |= ti.mipmapEnabled != config.generateMipMaps;
            }
            if (config.applyWrapMode)
            {
                changed |= ti.wrapMode != config.wrapMode;
            }
            if (config.applyFilterMode)
            {
                changed |= ti.filterMode != config.filterMode;
            }
            if (config.applyCompression)
            {
                changed |= ti.textureCompression != config.compression;
            }
            if (config.applyCrunchCompression)
            {
                changed |= ti.crunchedCompression != config.useCrunchCompression;
            }

            // Read current texture settings into a buffer for potential comparisons
            buffer ??= new TextureImporterSettings();
            ti.ReadTextureSettings(buffer);

            // Default platform settings
            TextureImporterPlatformSettings ps = ti.GetDefaultPlatformTextureSettings();
            if (config.applyPlatformResizeAlgorithm)
            {
                changed |= ps.resizeAlgorithm != config.platformResizeAlgorithm;
            }
            if (config.applyPlatformMaxTextureSize)
            {
                changed |= ps.maxTextureSize != config.platformMaxTextureSize;
            }
            if (config.applyPlatformFormat)
            {
                changed |= ps.format != config.platformFormat;
            }
            if (config.applyPlatformCompression)
            {
                changed |= ps.textureCompression != config.platformCompression;
            }
            if (config.applyPlatformCrunchCompression)
            {
                changed |= ps.crunchedCompression != config.platformUseCrunchCompression;
            }

            // Named platform overrides
            if (config.platformOverrides != null)
            {
                for (int i = 0; i < config.platformOverrides.Length; i++)
                {
                    PlatformOverride po = config.platformOverrides[i];
                    if (string.IsNullOrEmpty(po.name))
                    {
                        continue;
                    }
                    TextureImporterPlatformSettings ops =
                        po.name == TexturePlatformNameHelper.DefaultPlatformName
                            ? ti.GetDefaultPlatformTextureSettings()
                            : ti.GetPlatformTextureSettings(po.name);

                    if (po.applyResizeAlgorithm)
                    {
                        changed |= ops.resizeAlgorithm != po.resizeAlgorithm;
                    }
                    if (po.applyMaxTextureSize)
                    {
                        changed |= ops.maxTextureSize != po.maxTextureSize;
                    }
                    if (po.applyFormat)
                    {
                        changed |= ops.format != po.format;
                    }
                    if (po.applyCompression)
                    {
                        changed |= ops.textureCompression != po.compression;
                    }
                    if (po.applyCrunchCompression)
                    {
                        changed |= ops.crunchedCompression != po.useCrunchCompression;
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

            // Importer-level fields
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

            // Buffer for SetTextureSettings if we need it later (kept for parity/extension)
            buffer ??= new TextureImporterSettings();
            localTextureImporter.ReadTextureSettings(buffer);

            // Default platform settings
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

            // Named platform overrides
            if (config.platformOverrides != null)
            {
                for (int i = 0; i < config.platformOverrides.Length; i++)
                {
                    PlatformOverride po = config.platformOverrides[i];
                    if (string.IsNullOrEmpty(po.name))
                    {
                        continue;
                    }
                    TextureImporterPlatformSettings ops =
                        po.name == TexturePlatformNameHelper.DefaultPlatformName
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
                        // Ensure override is enabled for named platforms
                        ops.overridden =
                            po.name != TexturePlatformNameHelper.DefaultPlatformName
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
    }
#endif
}
