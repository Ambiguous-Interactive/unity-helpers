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
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>
    /// Public API to apply SpriteSettings profiles to assets. Mirrors the window logic
    /// but can be called from tests and scripts without UI.
    /// </summary>
    public static class SpriteSettingsApplierAPI
    {
        public sealed class PreparedProfile
        {
            public SpriteSettings settings;
            public SpriteSettings.MatchMode mode;
            public string nameLower;
            public string patternLower;
            public string extWithDot;
            public Regex regex;
            public int priority;
        }

        public static List<PreparedProfile> PrepareProfiles(List<SpriteSettings> profiles)
        {
            List<PreparedProfile> result = new(profiles?.Count ?? 0);
            if (profiles == null)
            {
                return result;
            }

            for (int i = 0; i < profiles.Count; i++)
            {
                SpriteSettings s = profiles[i];
                if (s == null)
                {
                    continue;
                }

                string trimmedPattern = string.IsNullOrEmpty(s.matchPattern)
                    ? null
                    : s.matchPattern.Trim();

                PreparedProfile p = new()
                {
                    settings = s,
                    mode = s.matchBy,
                    nameLower = string.IsNullOrEmpty(s.name) ? null : s.name.ToLowerInvariant(),
                    patternLower = string.IsNullOrEmpty(trimmedPattern)
                        ? null
                        : trimmedPattern.ToLowerInvariant(),
                    extWithDot =
                        string.IsNullOrEmpty(trimmedPattern) ? null
                        : trimmedPattern.StartsWith(".") ? trimmedPattern
                        : "." + trimmedPattern,
                    priority = s.priority,
                };
                if (
                    s.matchBy == SpriteSettings.MatchMode.Regex
                    && !string.IsNullOrEmpty(trimmedPattern)
                )
                {
                    try
                    {
                        p.regex = new Regex(
                            trimmedPattern,
                            RegexOptions.IgnoreCase | RegexOptions.Compiled
                        );
                    }
                    catch
                    {
                        p.regex = null;
                    }
                }
                result.Add(p);
            }
            return result;
        }

        private static string SanitizePath(string p)
        {
            return string.IsNullOrEmpty(p) ? p : p.SanitizePath();
        }

        /// <summary>
        /// Finds the highest-priority profile matching the asset path.
        /// </summary>
        /// <param name="assetPath">The asset path to match. Returns null if null/empty.</param>
        /// <param name="prepared">Prepared profiles to search. Returns null if null/empty.</param>
        /// <returns>The matching settings, or null if no match or invalid input.</returns>
        public static SpriteSettings FindMatchingSettings(
            string assetPath,
            List<PreparedProfile> prepared
        )
        {
            assetPath = SanitizePath(assetPath);
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            if (prepared == null || prepared.Count == 0)
            {
                return null;
            }

            string fileName = Path.GetFileName(assetPath);
            string fileNameLower = fileName.ToLowerInvariant();
            string pathLower = assetPath.ToLowerInvariant();
            string ext = Path.GetExtension(assetPath);

            SpriteSettings best = null;
            int bestPriority = int.MinValue;
            for (int i = 0; i < prepared.Count; i++)
            {
                PreparedProfile p = prepared[i];
                bool matches = false;
                switch (p.mode)
                {
#pragma warning disable CS0618 // Type or member is obsolete
                    case SpriteSettings.MatchMode.None:
#pragma warning restore CS0618 // Type or member is obsolete
                        break;
                    case SpriteSettings.MatchMode.Any:
                        matches =
                            string.IsNullOrEmpty(p.nameLower)
                            || fileNameLower.Contains(p.nameLower);
                        break;
                    case SpriteSettings.MatchMode.NameContains:
                        matches =
                            !string.IsNullOrEmpty(p.patternLower)
                            && fileNameLower.Contains(p.patternLower);
                        break;
                    case SpriteSettings.MatchMode.PathContains:
                        matches =
                            !string.IsNullOrEmpty(p.patternLower)
                            && pathLower.Contains(p.patternLower);
                        break;
                    case SpriteSettings.MatchMode.Extension:
                        matches =
                            !string.IsNullOrEmpty(p.extWithDot)
                            && string.Equals(ext, p.extWithDot, StringComparison.OrdinalIgnoreCase);
                        break;
                    case SpriteSettings.MatchMode.Regex:
                        matches = p.regex != null && p.regex.IsMatch(assetPath);
                        break;
                }
                if (!matches)
                {
                    continue;
                }

                if (best == null || p.priority > bestPriority)
                {
                    best = p.settings;
                    bestPriority = p.priority;
                }
            }
            return best;
        }

        /// <summary>
        /// Snapshot of the texture-import values that <see cref="WouldTextureSettingsChange"/>
        /// compares against a profile. Captures both the live <see cref="TextureImporter"/>
        /// properties and the <see cref="TextureImporterSettings"/> fields read during a
        /// change check, so the decision can be exercised by fast unit tests
        /// (<c>SpriteSettingsApplierLogicTests</c>) without importing a texture asset.
        /// </summary>
        internal readonly struct TextureSettingsState
        {
            // Read from the live TextureImporter.
            public readonly float SpritePixelsPerUnit;
            public readonly Vector2 SpritePivot;
            public readonly bool MipmapEnabled;
            public readonly bool CrunchedCompression;
            public readonly TextureImporterCompression TextureCompression;
            public readonly TextureImporterType TextureType;
            public readonly SpriteImportMode SpriteImportMode;

            // Read from TextureImporterSettings (ReadTextureSettings buffer).
            public readonly int SpriteAlignment;
            public readonly bool AlphaIsTransparency;
            public readonly bool Readable;
            public readonly int SpriteMode;
            public readonly uint SpriteExtrude;
            public readonly TextureWrapMode WrapMode;
            public readonly FilterMode FilterMode;

            public TextureSettingsState(
                float spritePixelsPerUnit,
                Vector2 spritePivot,
                bool mipmapEnabled,
                bool crunchedCompression,
                TextureImporterCompression textureCompression,
                TextureImporterType textureType,
                SpriteImportMode spriteImportMode,
                int spriteAlignment,
                bool alphaIsTransparency,
                bool readable,
                int spriteMode,
                uint spriteExtrude,
                TextureWrapMode wrapMode,
                FilterMode filterMode
            )
            {
                SpritePixelsPerUnit = spritePixelsPerUnit;
                SpritePivot = spritePivot;
                MipmapEnabled = mipmapEnabled;
                CrunchedCompression = crunchedCompression;
                TextureCompression = textureCompression;
                TextureType = textureType;
                SpriteImportMode = spriteImportMode;
                SpriteAlignment = spriteAlignment;
                AlphaIsTransparency = alphaIsTransparency;
                Readable = readable;
                SpriteMode = spriteMode;
                SpriteExtrude = spriteExtrude;
                WrapMode = wrapMode;
                FilterMode = filterMode;
            }
        }

        /// <summary>
        /// Pure decision: given a snapshot of the current texture-import state and a matched
        /// profile, returns whether applying the profile would change any value. Performs NO
        /// AssetDatabase/importer I/O, so it is exercised by fast unit tests instead of full
        /// texture-import round-trips. This is the behavior previously inlined in
        /// <see cref="WillTextureSettingsChange"/>.
        /// </summary>
        internal static bool WouldTextureSettingsChange(
            in TextureSettingsState current,
            SpriteSettings spriteData
        )
        {
            if (spriteData == null)
            {
                return false;
            }

            bool changed = false;
            if (spriteData.applyPixelsPerUnit)
            {
                changed |= current.SpritePixelsPerUnit != spriteData.pixelsPerUnit;
            }
            if (spriteData.applyPivot)
            {
                changed |= current.SpritePivot != spriteData.pivot;
            }
            if (spriteData.applyGenerateMipMaps)
            {
                changed |= current.MipmapEnabled != spriteData.generateMipMaps;
            }
            if (spriteData.applyCrunchCompression)
            {
                changed |= current.CrunchedCompression != spriteData.useCrunchCompression;
            }
            if (spriteData.applyCompression)
            {
                changed |= current.TextureCompression != spriteData.compressionLevel;
            }

            if (spriteData.applyTextureType)
            {
                changed |= current.TextureType != spriteData.textureType;
            }
            if (spriteData.applyPivot)
            {
                changed |= current.SpriteAlignment != (int)SpriteAlignment.Custom;
            }
            if (spriteData.applyAlphaIsTransparency)
            {
                changed |= current.AlphaIsTransparency != spriteData.alphaIsTransparency;
            }
            if (spriteData.applyReadWriteEnabled)
            {
                changed |= current.Readable != spriteData.readWriteEnabled;
            }
            if (spriteData.applySpriteMode)
            {
                changed |= current.SpriteImportMode != spriteData.spriteMode;
                changed |= current.SpriteMode != (int)spriteData.spriteMode;
            }
            if (spriteData.applyExtrudeEdges)
            {
                changed |= current.SpriteExtrude != spriteData.extrudeEdges;
            }
            if (spriteData.applyWrapMode)
            {
                changed |= current.WrapMode != spriteData.wrapMode;
            }
            if (spriteData.applyFilterMode)
            {
                changed |= current.FilterMode != spriteData.filterMode;
            }
            return changed;
        }

        /// <summary>
        /// Determines if applying sprite settings would change texture import settings.
        /// </summary>
        /// <param name="assetPath">The asset path to check. Returns false if null/empty/missing.</param>
        /// <param name="prepared">Prepared profiles to search for matching settings. Returns false if null.</param>
        /// <param name="buffer">Optional buffer for reading texture settings. If null, a new one will be created.</param>
        /// <returns>True if changes would occur, false otherwise.</returns>
        public static bool WillTextureSettingsChange(
            string assetPath,
            List<PreparedProfile> prepared,
            TextureImporterSettings buffer = null
        )
        {
            assetPath = SanitizePath(assetPath);
            TextureImporter textureImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (textureImporter == null)
            {
                return false;
            }

            // Use Unity's canonical assetPath for matching to avoid path separator issues.
            string realPath = textureImporter.assetPath;
            SpriteSettings spriteData = FindMatchingSettings(realPath, prepared);
            if (spriteData == null)
            {
                return false;
            }

            buffer ??= new TextureImporterSettings();
            textureImporter.ReadTextureSettings(buffer);

            TextureSettingsState current = new(
                textureImporter.spritePixelsPerUnit,
                textureImporter.spritePivot,
                textureImporter.mipmapEnabled,
                textureImporter.crunchedCompression,
                textureImporter.textureCompression,
                textureImporter.textureType,
                textureImporter.spriteImportMode,
                buffer.spriteAlignment,
                buffer.alphaIsTransparency,
                buffer.readable,
                buffer.spriteMode,
                buffer.spriteExtrude,
                buffer.wrapMode,
                buffer.filterMode
            );

            return WouldTextureSettingsChange(in current, spriteData);
        }

        /// <summary>
        /// Applies sprite settings to texture import settings.
        /// </summary>
        /// <param name="assetPath">The asset path to update. Returns false if null/empty/missing.</param>
        /// <param name="prepared">Prepared profiles to search for matching settings. Returns false if null.</param>
        /// <param name="textureImporter">The texture importer that was updated, or null if no update occurred.</param>
        /// <param name="buffer">Optional buffer for reading/writing texture settings. If null, a new one will be created.</param>
        /// <returns>True if changes were applied, false otherwise.</returns>
        public static bool TryUpdateTextureSettings(
            string assetPath,
            List<PreparedProfile> prepared,
            out TextureImporter textureImporter,
            TextureImporterSettings buffer = null
        )
        {
            assetPath = SanitizePath(assetPath);
            textureImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (textureImporter == null)
            {
                return false;
            }

            // Use Unity's canonical assetPath for matching to avoid path separator issues.
            string realPath = textureImporter.assetPath;
            SpriteSettings spriteData = FindMatchingSettings(realPath, prepared);
            if (spriteData == null)
            {
                return false;
            }

            bool changed = false;
            bool settingsChanged = false;
            bool undoRecorded = false;

            TextureImporter localTextureImporter = textureImporter;

            buffer ??= new TextureImporterSettings();
            textureImporter.ReadTextureSettings(buffer);

            if (spriteData.applyTextureType)
            {
                if (textureImporter.textureType != spriteData.textureType)
                {
                    EnsureUndoRecorded();
                    textureImporter.textureType = spriteData.textureType;
                    changed = true;
                }
            }

            if (spriteData.applySpriteMode)
            {
                if (textureImporter.spriteImportMode != spriteData.spriteMode)
                {
                    EnsureUndoRecorded();
                    textureImporter.spriteImportMode = spriteData.spriteMode;
                    changed = true;
                }
                if (buffer.spriteMode != (int)spriteData.spriteMode)
                {
                    EnsureUndoRecorded();
                    buffer.spriteMode = (int)spriteData.spriteMode;
                    settingsChanged = true;
                }
            }
            if (spriteData.applyPixelsPerUnit)
            {
                if (textureImporter.spritePixelsPerUnit != spriteData.pixelsPerUnit)
                {
                    EnsureUndoRecorded();
                    textureImporter.spritePixelsPerUnit = spriteData.pixelsPerUnit;
                    changed = true;
                }
                if (buffer.spritePixelsPerUnit != spriteData.pixelsPerUnit)
                {
                    EnsureUndoRecorded();
                    buffer.spritePixelsPerUnit = spriteData.pixelsPerUnit;
                    settingsChanged = true;
                }
            }
            if (spriteData.applyPivot)
            {
                if (textureImporter.spritePivot != spriteData.pivot)
                {
                    EnsureUndoRecorded();
                    textureImporter.spritePivot = spriteData.pivot;
                    changed = true;
                }
                if (buffer.spriteAlignment != (int)SpriteAlignment.Custom)
                {
                    EnsureUndoRecorded();
                    buffer.spriteAlignment = (int)SpriteAlignment.Custom;
                    settingsChanged = true;
                }
                if (buffer.spritePivot != spriteData.pivot)
                {
                    EnsureUndoRecorded();
                    buffer.spritePivot = spriteData.pivot;
                    settingsChanged = true;
                }
            }
            if (spriteData.applyGenerateMipMaps)
            {
                if (textureImporter.mipmapEnabled != spriteData.generateMipMaps)
                {
                    EnsureUndoRecorded();
                    textureImporter.mipmapEnabled = spriteData.generateMipMaps;
                    changed = true;
                }
                if (buffer.mipmapEnabled != spriteData.generateMipMaps)
                {
                    EnsureUndoRecorded();
                    buffer.mipmapEnabled = spriteData.generateMipMaps;
                    settingsChanged = true;
                }
            }
            if (spriteData.applyCrunchCompression)
            {
                if (textureImporter.crunchedCompression != spriteData.useCrunchCompression)
                {
                    EnsureUndoRecorded();
                    textureImporter.crunchedCompression = spriteData.useCrunchCompression;
                    changed = true;
                }
            }
            if (spriteData.applyCompression)
            {
                if (textureImporter.textureCompression != spriteData.compressionLevel)
                {
                    EnsureUndoRecorded();
                    textureImporter.textureCompression = spriteData.compressionLevel;
                    changed = true;
                }
            }
            if (spriteData.applyAlphaIsTransparency)
            {
                if (textureImporter.alphaIsTransparency != spriteData.alphaIsTransparency)
                {
                    EnsureUndoRecorded();
                    textureImporter.alphaIsTransparency = spriteData.alphaIsTransparency;
                    changed = true;
                }
                if (buffer.alphaIsTransparency != spriteData.alphaIsTransparency)
                {
                    EnsureUndoRecorded();
                    buffer.alphaIsTransparency = spriteData.alphaIsTransparency;
                    settingsChanged = true;
                }
            }
            if (spriteData.applyReadWriteEnabled)
            {
                if (textureImporter.isReadable != spriteData.readWriteEnabled)
                {
                    EnsureUndoRecorded();
                    textureImporter.isReadable = spriteData.readWriteEnabled;
                    changed = true;
                }
                if (buffer.readable != spriteData.readWriteEnabled)
                {
                    EnsureUndoRecorded();
                    buffer.readable = spriteData.readWriteEnabled;
                    settingsChanged = true;
                }
            }
            if (spriteData.applyExtrudeEdges)
            {
                if (buffer.spriteExtrude != spriteData.extrudeEdges)
                {
                    EnsureUndoRecorded();
                    buffer.spriteExtrude = spriteData.extrudeEdges;
                    settingsChanged = true;
                }
            }
            if (spriteData.applyWrapMode)
            {
                if (textureImporter.wrapMode != spriteData.wrapMode)
                {
                    EnsureUndoRecorded();
                    textureImporter.wrapMode = spriteData.wrapMode;
                    changed = true;
                }
                if (buffer.wrapMode != spriteData.wrapMode)
                {
                    EnsureUndoRecorded();
                    buffer.wrapMode = spriteData.wrapMode;
                    settingsChanged = true;
                }
            }
            if (spriteData.applyFilterMode)
            {
                if (textureImporter.filterMode != spriteData.filterMode)
                {
                    EnsureUndoRecorded();
                    textureImporter.filterMode = spriteData.filterMode;
                    changed = true;
                }
                if (buffer.filterMode != spriteData.filterMode)
                {
                    EnsureUndoRecorded();
                    buffer.filterMode = spriteData.filterMode;
                    settingsChanged = true;
                }
            }

            if (settingsChanged)
            {
                EnsureUndoRecorded();
                textureImporter.SetTextureSettings(buffer);
            }

            return changed || settingsChanged;

            void EnsureUndoRecorded()
            {
                if (undoRecorded)
                {
                    return;
                }

                Undo.RecordObject(localTextureImporter, "Apply Sprite Settings");
                undoRecorded = true;
            }
        }
    }
#endif
}
