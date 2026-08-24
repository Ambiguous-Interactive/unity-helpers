// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using UnityEngine;

    /// <summary>
    /// JSON-serializable configuration for sprite sheet extraction settings.
    /// Stored alongside sprite sheet textures as {texturePath}.spritesheet.json.
    /// </summary>
    [Serializable]
    public sealed class SpriteSheetConfig
    {
        /// <summary>
        /// Configuration file extension (without the leading dot).
        /// </summary>
        public const string FileExtension = "spritesheet.json";

        /// <summary>
        /// Current configuration version for migration support.
        /// </summary>
        public const int CurrentVersion = 3;

        /// <summary>
        /// Configuration version number for future migration support.
        /// </summary>
        public int version = CurrentVersion;

        /// <summary>
        /// The pivot mode for extracted sprites.
        /// </summary>
        public PivotMode pivotMode = PivotMode.Center;

        /// <summary>
        /// Custom pivot point when pivotMode is set to Custom.
        /// Values are normalized (0-1) where (0,0) is bottom-left and (1,1) is top-right.
        /// </summary>
        public Vector2 customPivot = new(0.5f, 0.5f);

        /// <summary>
        /// The auto-detection algorithm to use.
        /// Stored as int for JSON serialization compatibility.
        /// </summary>
        public int algorithm = (int)AutoDetectionAlgorithm.AutoBest;

        /// <summary>
        /// Expected number of sprites in the sheet. -1 indicates not set.
        /// Used for validation and uniform grid detection.
        /// </summary>
        public int expectedSpriteCount = -1;

        /// <summary>
        /// SHA256 hash of the texture file bytes for change detection.
        /// When the texture content changes, this hash will no longer match,
        /// indicating the configuration may be stale.
        /// </summary>
        public string textureContentHash;

        /// <summary>
        /// Cached algorithm results for this texture.
        /// Invalidated when textureContentHash changes.
        /// </summary>
        public CachedAlgorithmResult cachedAlgorithmResult;

        /// <summary>
        /// Whether to snap detected cell sizes to exact divisors of texture dimensions
        /// using transparency-aware analysis. When true, adjusts cell sizes to align
        /// grid lines with transparent regions for cleaner sprite boundaries.
        /// </summary>
        public bool snapToTextureDivisor = true;

        /// <summary>
        /// Gets the config file path for a given texture path.
        /// </summary>
        /// <param name="texturePath">The path to the source texture.</param>
        /// <returns>The config file path with .spritesheet.json extension.</returns>
        public static string GetConfigPath(string texturePath)
        {
            if (string.IsNullOrEmpty(texturePath))
            {
                return string.Empty;
            }
            return texturePath + "." + FileExtension;
        }

        /// <summary>
        /// Migrates a configuration from an older version to the current version.
        /// Currently a no-op for version 1, but provides the hook for future migrations.
        /// </summary>
        /// <param name="config">The configuration to migrate.</param>
        public static void MigrateConfig(SpriteSheetConfig config)
        {
            if (config == null || CurrentVersion <= config.version)
            {
                return;
            }

            // Migrate v1 -> v2: Add algorithm caching
            if (config.version < 2)
            {
                config.algorithm = (int)AutoDetectionAlgorithm.AutoBest;
                config.cachedAlgorithmResult = null;
                config.version = 2;
            }

            // Migrate v2 -> v3: Add snapToTextureDivisor persistence
            if (config.version < 3)
            {
                config.snapToTextureDivisor = true;
                config.version = 3;
            }

            config.version = CurrentVersion;
        }
    }

    /// <summary>
    /// Cached result from an auto-detection algorithm run.
    /// Stored in the config file for cross-session persistence.
    /// </summary>
    [Serializable]
    public sealed class CachedAlgorithmResult
    {
        /// <summary>
        /// The algorithm that produced this result.
        /// </summary>
        public int algorithm;

        /// <summary>
        /// Detected cell width in pixels.
        /// </summary>
        public int cellWidth;

        /// <summary>
        /// Detected cell height in pixels.
        /// </summary>
        public int cellHeight;

        /// <summary>
        /// Confidence score in the range [0, 1].
        /// </summary>
        public float confidence;

        /// <summary>
        /// Creates a cached result from an algorithm result.
        /// </summary>
        public static CachedAlgorithmResult FromResult(SpriteSheetAlgorithms.AlgorithmResult result)
        {
            return new CachedAlgorithmResult
            {
                algorithm = (int)result.Algorithm,
                cellWidth = result.CellWidth,
                cellHeight = result.CellHeight,
                confidence = result.Confidence,
            };
        }

        /// <summary>
        /// Converts this cached result to an algorithm result.
        /// </summary>
        public SpriteSheetAlgorithms.AlgorithmResult ToResult()
        {
            return new SpriteSheetAlgorithms.AlgorithmResult(
                cellWidth,
                cellHeight,
                confidence,
                (AutoDetectionAlgorithm)algorithm
            );
        }
    }
#endif
}
