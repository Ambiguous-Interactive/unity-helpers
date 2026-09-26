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
    using WallstopStudios.UnityHelpers.Core.Serialization;

    /// <summary>
    /// Public API to apply SpriteSettings profiles to assets. Mirrors the window logic
    /// but can be called from tests and scripts without UI.
    /// </summary>
    public static class SpriteSettingsApplierAPI
    {
        internal static Func<string, bool> DeleteProfileAssetAction = AssetDatabase.DeleteAsset;
        internal static Action<UnityEngine.Object, string> CreateProfileAssetAction =
            AssetDatabase.CreateAsset;
        internal static Action<string> BeforeAbsentRestoreMoveForTests;
        internal static Action SaveProfileAssetsAction = AssetDatabase.SaveAssets;
        internal static Func<string, string, string> MoveProfileAssetAction =
            AssetDatabase.MoveAsset;
        internal static Action<string, ImportAssetOptions> ImportProfileAssetAction =
            AssetDatabase.ImportAsset;
        internal static Func<string, SpriteSettingsProfileCollection> LoadProfileAssetAction =
            AssetDatabase.LoadAssetAtPath<SpriteSettingsProfileCollection>;

        /// <summary>Saves independent copies of profiles to a project asset.</summary>
        /// <param name="assetPath">A path under Assets ending in .asset whose parent folder exists.</param>
        /// <param name="profiles">Profiles to save.</param>
        /// <param name="overwriteExisting">Whether an existing profile collection may be replaced.</param>
        /// <param name="error">The failure reason, or null on success.</param>
        /// <returns>Whether the profiles were saved.</returns>
        /// <remarks>
        /// Asset file writes and reimports cannot be fully reversed by Unity Undo. An overwrite
        /// refuses a file changed before comparison; noncooperating external writers can still
        /// race with the replacement itself.
        /// </remarks>
        public static bool TrySaveProfiles(
            string assetPath,
            IReadOnlyList<SpriteSettings> profiles,
            bool overwriteExisting,
            out string error
        )
        {
            try
            {
                return TrySaveProfilesCore(assetPath, profiles, overwriteExisting, out error);
            }
            catch (Exception exception)
            {
                error = $"Could not save profiles: {exception.Message}";
                return false;
            }
        }

        /// <summary>Loads independent profile copies from a project asset.</summary>
        /// <param name="assetPath">The project asset path to load.</param>
        /// <param name="profiles">The loaded copies, or null on failure.</param>
        /// <param name="error">The failure reason, or null on success.</param>
        /// <returns>Whether profiles were loaded.</returns>
        public static bool TryLoadProfiles(
            string assetPath,
            out List<SpriteSettings> profiles,
            out string error
        )
        {
            try
            {
                return TryLoadProfilesCore(assetPath, out profiles, out error);
            }
            catch (Exception exception)
            {
                error = $"Could not load profiles: {exception.Message}";
                profiles = null;
                return false;
            }
        }

        public static List<PreparedProfile> PrepareProfiles(List<SpriteSettings> profiles)
        {
            List<PreparedProfile> result = new(profiles?.Count ?? 0);
            if (profiles == null)
            {
                return result;
            }

            foreach (SpriteSettings s in profiles)
            {
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
            foreach (PreparedProfile p in prepared)
            {
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

                if (best == null || bestPriority < p.priority)
                {
                    best = p.settings;
                    bestPriority = p.priority;
                }
            }
            return best;
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
        /// <param name="textureImporter">The importer at <paramref name="assetPath"/>, whether or not it was updated, and null only when the path carries none.</param>
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
            TextureImporter localTextureImporter =
                AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (localTextureImporter == null)
            {
                textureImporter = null;
                return false;
            }

            // Use Unity's canonical assetPath for matching to avoid path separator issues.
            string realPath = localTextureImporter.assetPath;
            SpriteSettings spriteData = FindMatchingSettings(realPath, prepared);
            if (spriteData == null)
            {
                // Return the usable importer even when no profile matches.
                textureImporter = localTextureImporter;
                return false;
            }

            bool changed = false;
            bool settingsChanged = false;
            bool undoRecorded = false;

            buffer ??= new TextureImporterSettings();
            localTextureImporter.ReadTextureSettings(buffer);

            if (spriteData.applyTextureType)
            {
                if (localTextureImporter.textureType != spriteData.textureType)
                {
                    EnsureUndoRecorded();
                    localTextureImporter.textureType = spriteData.textureType;
                    changed = true;
                }
            }

            if (spriteData.applySpriteMode)
            {
                if (localTextureImporter.spriteImportMode != spriteData.spriteMode)
                {
                    EnsureUndoRecorded();
                    localTextureImporter.spriteImportMode = spriteData.spriteMode;
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
                if (localTextureImporter.spritePixelsPerUnit != spriteData.pixelsPerUnit)
                {
                    EnsureUndoRecorded();
                    localTextureImporter.spritePixelsPerUnit = spriteData.pixelsPerUnit;
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
                if (localTextureImporter.spritePivot != spriteData.pivot)
                {
                    EnsureUndoRecorded();
                    localTextureImporter.spritePivot = spriteData.pivot;
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
                if (localTextureImporter.mipmapEnabled != spriteData.generateMipMaps)
                {
                    EnsureUndoRecorded();
                    localTextureImporter.mipmapEnabled = spriteData.generateMipMaps;
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
                if (localTextureImporter.crunchedCompression != spriteData.useCrunchCompression)
                {
                    EnsureUndoRecorded();
                    localTextureImporter.crunchedCompression = spriteData.useCrunchCompression;
                    changed = true;
                }
            }
            if (spriteData.applyCompression)
            {
                if (localTextureImporter.textureCompression != spriteData.compressionLevel)
                {
                    EnsureUndoRecorded();
                    localTextureImporter.textureCompression = spriteData.compressionLevel;
                    changed = true;
                }
            }
            if (spriteData.applyAlphaIsTransparency)
            {
                if (localTextureImporter.alphaIsTransparency != spriteData.alphaIsTransparency)
                {
                    EnsureUndoRecorded();
                    localTextureImporter.alphaIsTransparency = spriteData.alphaIsTransparency;
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
                if (localTextureImporter.isReadable != spriteData.readWriteEnabled)
                {
                    EnsureUndoRecorded();
                    localTextureImporter.isReadable = spriteData.readWriteEnabled;
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
                if (localTextureImporter.wrapMode != spriteData.wrapMode)
                {
                    EnsureUndoRecorded();
                    localTextureImporter.wrapMode = spriteData.wrapMode;
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
                if (localTextureImporter.filterMode != spriteData.filterMode)
                {
                    EnsureUndoRecorded();
                    localTextureImporter.filterMode = spriteData.filterMode;
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

                Undo.RecordObject(localTextureImporter, "Apply Sprite Settings");
                undoRecorded = true;
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

        private static bool TrySaveProfilesCore(
            string assetPath,
            IReadOnlyList<SpriteSettings> profiles,
            bool overwriteExisting,
            out string error
        )
        {
            if (!TryValidateProfilePath(assetPath, out string path, out error))
            {
                return false;
            }
            if (profiles == null)
            {
                error = "Profiles cannot be null.";
                return false;
            }

            List<SpriteSettings> copies = new(profiles.Count);
            foreach (SpriteSettings profile in profiles)
            {
                if (profile == null)
                {
                    error = "Profiles cannot contain null entries.";
                    return false;
                }
                try
                {
                    SpriteSettings copy = CloneProfile(profile);
                    if (copy == null)
                    {
                        error = "Could not clone a profile.";
                        return false;
                    }
                    copies.Add(copy);
                }
                catch (Exception exception)
                {
                    error = $"Could not clone a profile: {exception.Message}";
                    return false;
                }
            }

            int expectedProfilesCount = copies.Count;
            string expectedProfilesJson;
            try
            {
                expectedProfilesJson = Serializer.JsonStringify(copies);
            }
            catch (Exception exception)
            {
                error = $"Could not serialize profiles for verification: {exception.Message}";
                return false;
            }

            string fullPath = ProfileFullPath(path);
            UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(path);
            bool occupied = File.Exists(fullPath) || mainAsset != null;
            SpriteSettingsProfileCollection existing = null;
            if (occupied)
            {
                existing = mainAsset as SpriteSettingsProfileCollection;
                if (existing == null)
                {
                    error = $"An asset of another type already exists at {path}.";
                    return false;
                }
                if (!overwriteExisting)
                {
                    error =
                        $"A profiles asset already exists at {path}; set overwriteExisting to replace it.";
                    return false;
                }
            }

            string writePath = Path.ChangeExtension(path, $".staging.{Guid.NewGuid():N}.asset");
            byte[] previousBytes = existing == null ? null : File.ReadAllBytes(fullPath);
            byte[] stagedBytes = null;
            bool replacementAttempted = false;
            bool replacementSucceeded = false;
            bool creationReturned = false;
            bool moveCommitted = false;
            string ownedGuid = null;
            byte[] ownedBytes = null;
            SpriteSettingsProfileCollection target = null;
            try
            {
                target = ScriptableObject.CreateInstance<SpriteSettingsProfileCollection>();
                target.profiles = copies;
                target.name =
                    existing != null ? existing.name : Path.GetFileNameWithoutExtension(path);
                CreateProfileAssetAction(target, writePath);
                creationReturned = true;
                ownedGuid = AssetDatabase.AssetPathToGUID(writePath);
                if (string.IsNullOrEmpty(ownedGuid))
                {
                    throw new IOException($"Could not identify profiles asset at {writePath}.");
                }
                SaveProfileAssetsAction();
                ownedBytes = File.ReadAllBytes(ProfileFullPath(writePath));
                if (LoadProfileAssetAction(writePath) == null)
                {
                    throw new IOException($"Could not create profiles asset at {writePath}.");
                }
                if (existing == null)
                {
                    string moveError;
                    try
                    {
                        moveError = MoveProfileAssetAction(writePath, path);
                    }
                    catch (Exception moveException)
                    {
                        moveError = moveException.Message;
                    }
                    bool moved = string.Equals(
                        AssetDatabase.AssetPathToGUID(path),
                        ownedGuid,
                        StringComparison.Ordinal
                    );
                    if (!moved)
                    {
                        throw new IOException(
                            $"Could not move profiles asset to {path}: {moveError ?? "destination did not receive the asset"}"
                        );
                    }
                    moveCommitted = true;
                    ImportProfileAssetAction(path, ImportAssetOptions.ForceSynchronousImport);
                    SpriteSettingsProfileCollection committed = LoadProfileAssetAction(path);
                    if (
                        !ProfilesMatchExpected(
                            committed,
                            expectedProfilesCount,
                            expectedProfilesJson
                        )
                    )
                    {
                        throw new IOException(
                            $"Profiles asset moved to {path} but could not be verified; inspect the destination."
                        );
                    }
                    error = null;
                    return true;
                }
                else
                {
                    stagedBytes = File.ReadAllBytes(ProfileFullPath(writePath));
                    if (!TryDeleteProfileAsset(writePath, out string cleanupError))
                    {
                        throw new IOException(cleanupError);
                    }
                    replacementAttempted = true;
                    if (
                        !DurableFile.TryCompareThenReplaceBytes(
                            fullPath,
                            previousBytes,
                            stagedBytes,
                            out Exception writeError
                        )
                    )
                    {
                        throw new IOException(
                            $"Could not overwrite profiles asset: {writeError.Message}"
                        );
                    }
                    replacementSucceeded = true;
                }
                ImportProfileAssetAction(path, ImportAssetOptions.ForceSynchronousImport);
                if (
                    !ProfilesMatchExpected(
                        LoadProfileAssetAction(path),
                        expectedProfilesCount,
                        expectedProfilesJson
                    )
                )
                {
                    throw new IOException($"Profiles asset at {path} could not be verified.");
                }
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                string failure = $"Could not save profiles: {exception.Message}";
                if (moveCommitted)
                {
                    failure += $" Inspect the destination at {path} before retrying.";
                }
                if (replacementSucceeded || (replacementAttempted && !File.Exists(fullPath)))
                {
                    try
                    {
                        bool restored = replacementSucceeded
                            ? DurableFile.TryCompareThenReplaceBytes(
                                fullPath,
                                stagedBytes,
                                previousBytes,
                                out Exception restoreError
                            )
                            : TryRestoreAbsentProfileBytes(
                                fullPath,
                                previousBytes,
                                out restoreError
                            );
                        if (!restored)
                        {
                            failure +=
                                $" Could not restore the prior asset: {restoreError.Message}";
                        }
                        else
                        {
                            AssetDatabase.ImportAsset(
                                path,
                                ImportAssetOptions.ForceSynchronousImport
                            );
                        }
                    }
                    catch (Exception restoreException)
                    {
                        failure +=
                            $" Could not restore the prior asset: {restoreException.Message}";
                    }
                }
                try
                {
                    if (
                        creationReturned
                        && !TryRemoveOwnedProfileAsset(
                            writePath,
                            ownedGuid,
                            ownedBytes,
                            target,
                            out string cleanupError
                        )
                    )
                    {
                        failure += $" {cleanupError}";
                    }
                    if (
                        !creationReturned
                        && (
                            File.Exists(ProfileFullPath(writePath))
                            || AssetDatabase.LoadMainAssetAtPath(writePath) != null
                        )
                    )
                    {
                        failure +=
                            $" Partial profiles asset at {writePath} may have changed; inspect it before removal.";
                    }
                }
                catch (Exception cleanupException)
                {
                    failure += $" Could not remove partial asset: {cleanupException.Message}";
                }
                try
                {
                    if (target != null && !AssetDatabase.Contains(target))
                    {
                        UnityEngine.Object.DestroyImmediate(target);
                    }
                }
                catch (Exception destroyException)
                {
                    failure +=
                        $" Could not release temporary profile object: {destroyException.Message}";
                }
                error = failure;
                return false;
            }
        }

        private static bool ProfilesMatchExpected(
            SpriteSettingsProfileCollection committed,
            int expectedCount,
            string expectedJson
        )
        {
            return committed != null
                && committed.profiles != null
                && committed.profiles.Count == expectedCount
                && string.Equals(
                    Serializer.JsonStringify(committed.profiles),
                    expectedJson,
                    StringComparison.Ordinal
                );
        }

        private static bool TryRestoreAbsentProfileBytes(
            string fullPath,
            byte[] bytes,
            out Exception error
        )
        {
            string restorePath = fullPath + $".restore.{Guid.NewGuid():N}";
            Exception failure = null;
            try
            {
                using (
                    FileStream stream = new(
                        restorePath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None
                    )
                )
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(flushToDisk: true);
                }
                BeforeAbsentRestoreMoveForTests?.Invoke(fullPath);
                File.Move(restorePath, fullPath);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            try
            {
                File.Delete(restorePath);
            }
            catch (Exception cleanupException)
            {
                failure ??= cleanupException;
            }
            error = failure;
            return failure == null;
        }

        private static bool TryRemoveOwnedProfileAsset(
            string stagingPath,
            string ownedGuid,
            byte[] ownedBytes,
            SpriteSettingsProfileCollection target,
            out string error
        )
        {
            if (target == null)
            {
                error = null;
                return true;
            }
            string candidate = null;
            if (
                !string.IsNullOrEmpty(ownedGuid)
                && string.Equals(
                    AssetDatabase.AssetPathToGUID(stagingPath),
                    ownedGuid,
                    StringComparison.Ordinal
                )
            )
            {
                candidate = stagingPath;
            }
            if (candidate == null)
            {
                string objectPath = AssetDatabase.GetAssetPath(target);
                if (
                    string.Equals(objectPath, stagingPath, StringComparison.Ordinal)
                    && AssetDatabase.LoadMainAssetAtPath(objectPath) == target
                )
                {
                    candidate = objectPath;
                }
            }
            if (candidate == null)
            {
                error = null;
                return true;
            }
            string fullPath = ProfileFullPath(candidate);
            if (ownedBytes == null)
            {
                error =
                    $"Could not verify partial profiles asset at {candidate}; inspect it before removal.";
                return false;
            }
            if (
                File.Exists(fullPath)
                && !File.ReadAllBytes(fullPath).AsSpan().SequenceEqual(ownedBytes)
            )
            {
                error =
                    $"Could not remove partial profiles asset at {candidate}: contents changed.";
                return false;
            }
            return TryDeleteProfileAsset(candidate, out error);
        }

        private static bool TryDeleteProfileAsset(string assetPath, out string error)
        {
            bool deleted = DeleteProfileAssetAction(assetPath);
            string fullPath = ProfileFullPath(assetPath);
            if (
                !deleted
                || File.Exists(fullPath)
                || AssetDatabase.LoadMainAssetAtPath(assetPath) != null
            )
            {
                error = $"Could not remove partial profiles asset at {assetPath}.";
                return false;
            }
            error = null;
            return true;
        }

        private static string ProfileFullPath(string assetPath)
        {
            return Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath);
        }

        private static bool TryLoadProfilesCore(
            string assetPath,
            out List<SpriteSettings> profiles,
            out string error
        )
        {
            if (!TryValidateProfilePath(assetPath, out string path, out error))
            {
                profiles = null;
                return false;
            }
            UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(path);
            SpriteSettingsProfileCollection asset = mainAsset as SpriteSettingsProfileCollection;
            if (asset == null)
            {
                string fullPath = ProfileFullPath(path);
                error =
                    File.Exists(fullPath) || mainAsset != null
                        ? $"Asset at {path} is not a SpriteSettingsProfileCollection."
                        : $"No profiles asset exists at {path}.";
                profiles = null;
                return false;
            }
            if (asset.profiles == null)
            {
                error = $"Profiles asset at {path} contains no profile list.";
                profiles = null;
                return false;
            }
            List<SpriteSettings> copies = new(asset.profiles.Count);
            try
            {
                foreach (SpriteSettings profile in asset.profiles)
                {
                    if (profile == null)
                    {
                        error = $"Profiles asset at {path} contains a null profile.";
                        profiles = null;
                        return false;
                    }
                    SpriteSettings copy = CloneProfile(profile);
                    if (copy == null)
                    {
                        error = $"Could not clone a profile from {path}.";
                        profiles = null;
                        return false;
                    }
                    copies.Add(copy);
                }
            }
            catch (Exception exception)
            {
                error = $"Could not clone loaded profiles: {exception.Message}";
                profiles = null;
                return false;
            }
            profiles = copies;
            error = null;
            return true;
        }

        private static SpriteSettings CloneProfile(SpriteSettings profile)
        {
            return Serializer.JsonDeserialize<SpriteSettings>(Serializer.JsonStringify(profile));
        }

        private static bool TryValidateProfilePath(
            string assetPath,
            out string path,
            out string error
        )
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                path = null;
                error = "An asset path is required.";
                return false;
            }
            string candidate = assetPath.SanitizePath();
            if (
                !candidate.StartsWith("Assets/", StringComparison.Ordinal)
                || !string.Equals(
                    Path.GetExtension(candidate),
                    ".asset",
                    StringComparison.OrdinalIgnoreCase
                )
                || candidate.Contains("/../")
                || candidate.Contains("/./")
                || !AssetDatabase.IsValidFolder(Path.GetDirectoryName(candidate).SanitizePath())
            )
            {
                path = null;
                error = $"Expected an .asset path in an existing folder under Assets: {assetPath}.";
                return false;
            }
            path = candidate;
            error = null;
            return true;
        }

        private static string SanitizePath(string p)
        {
            return string.IsNullOrEmpty(p) ? p : p.SanitizePath();
        }

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

        /// <summary>
        /// Snapshot of the texture-import values that <see cref="WouldTextureSettingsChange"/>
        /// compares against a profile. Captures both the live <see cref="TextureImporter"/>
        /// properties and the <see cref="TextureImporterSettings"/> fields read during a
        /// change check, so the decision can be exercised by fast unit tests
        /// (<c>SpriteSettingsApplierLogicTests</c>) without importing a texture asset.
        /// </summary>
        internal readonly struct TextureSettingsState
        {
            public readonly float SpritePixelsPerUnit;
            public readonly Vector2 SpritePivot;
            public readonly bool MipmapEnabled;
            public readonly bool CrunchedCompression;
            public readonly TextureImporterCompression TextureCompression;
            public readonly TextureImporterType TextureType;
            public readonly SpriteImportMode SpriteImportMode;

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
    }
#endif
}
