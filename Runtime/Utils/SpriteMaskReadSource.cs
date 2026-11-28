// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;

    /// <summary>Selects which texture <see cref="SpriteMaskReader"/> reads in the Editor.</summary>
    public enum SpriteMaskReadSource
    {
        /// <summary>Invalid or unset source.</summary>
        [Obsolete("Use SourcePng or ImportedTexture instead of Unknown.")]
        Unknown = 0,

        /// <summary>Reads the original source PNG, honoring the importer's alpha source.</summary>
        SourcePng = 1,

        /// <summary>Reads the imported, readable texture that the player draws.</summary>
        ImportedTexture = 2,
    }
}
