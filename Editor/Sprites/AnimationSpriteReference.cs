// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
#if UNITY_EDITOR
    using System;

    /// <summary>
    /// Persistent identity of a sprite, including a sprite within a shared asset.
    /// </summary>
    [Serializable]
    public sealed class AnimationSpriteReference
    {
        /// <summary>
        /// GUID of the containing asset, preserved when the asset moves.
        /// </summary>
        public string guid = string.Empty;

        /// <summary>
        /// Local file identifier of the exact sprite within its asset.
        /// </summary>
        public long localFileId;
    }
#endif
}
