// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
    using UnityEngine;

    /// <summary>Identifies a sprite assignment, its clip time, and its renderer path.</summary>
    public readonly struct SpriteAnimationKeyframe
    {
        /// <summary>Gets the keyframe time in seconds.</summary>
        public float Time { get; }

        /// <summary>Gets the assigned sprite, or null for an empty or destroyed assignment.</summary>
        public Sprite Sprite { get; }

        /// <summary>Gets the renderer path relative to the animated root.</summary>
        public string BindingPath { get; }

        internal SpriteAnimationKeyframe(float time, Sprite sprite, string bindingPath)
        {
            Time = time;
            Sprite = sprite;
            BindingPath = bindingPath;
        }
    }
}
