// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
    using System;

    /// <summary>Selects a sprite-local bounds edge for motion analysis.</summary>
    public enum SpriteBoundsEdge
    {
        /// <summary>Represents an unspecified edge.</summary>
        [Obsolete("Choose a specific sprite bounds edge.")]
        Unknown = 0,

        /// <summary>Measures bounds.min.x.</summary>
        Left = 1,

        /// <summary>Measures bounds.max.x.</summary>
        Right = 2,

        /// <summary>Measures bounds.min.y.</summary>
        Bottom = 3,

        /// <summary>Measures bounds.max.y.</summary>
        Top = 4,
    }
}
