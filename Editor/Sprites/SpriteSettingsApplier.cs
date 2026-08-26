// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// ReSharper disable CompareOfFloatsByEqualityOperator
namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    [Serializable]
    public sealed class SpriteSettings
    {
        public MatchMode matchBy = MatchMode.Any;
        public string matchPattern = string.Empty;
        public int priority;

        public bool applyPixelsPerUnit;

        [WShowIf(nameof(applyPixelsPerUnit))]
        public int pixelsPerUnit = 100;

        public bool applyPivot;

        [WShowIf(nameof(applyPivot))]
        public Vector2 pivot = new(0.5f, 0.5f);

        public bool applySpriteMode;

        [WShowIf(nameof(applySpriteMode))]
        public SpriteImportMode spriteMode = SpriteImportMode.Single;

        public bool applyGenerateMipMaps;

        [WShowIf(nameof(applyGenerateMipMaps))]
        public bool generateMipMaps;

        public bool applyAlphaIsTransparency;

        [WShowIf(nameof(applyAlphaIsTransparency))]
        public bool alphaIsTransparency = true;

        public bool applyReadWriteEnabled;

        [WShowIf(nameof(applyReadWriteEnabled))]
        public bool readWriteEnabled = true;

        public bool applyExtrudeEdges;

        [WShowIf(nameof(applyExtrudeEdges))]
        [Range(0, 32)]
        public uint extrudeEdges = 1;

        public bool applyWrapMode;

        [WShowIf(nameof(applyWrapMode))]
        public TextureWrapMode wrapMode = TextureWrapMode.Clamp;

        public bool applyFilterMode;

        [WShowIf(nameof(applyFilterMode))]
        public FilterMode filterMode = FilterMode.Point;

        public bool applyCrunchCompression;

        [WShowIf(nameof(applyCrunchCompression))]
        public bool useCrunchCompression;

        public bool applyCompression;

        [WShowIf(nameof(applyCompression))]
        [SerializeField]
        public TextureImporterCompression compressionLevel = TextureImporterCompression.Compressed;

        public string name = string.Empty;

        public bool applyTextureType;

        [WShowIf(nameof(applyTextureType))]
        public TextureImporterType textureType = TextureImporterType.Sprite;

        public enum MatchMode
        {
            [Obsolete("Default is invalid. Choose a specific match mode.", false)]
            None = 0,
            Any = 1,
            NameContains = 2,
            PathContains = 3,
            Regex = 4,
            Extension = 5,
        }
    }
#endif
}
