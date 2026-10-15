// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Sprites
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>Identifies a sprite assignment, its clip time, and its renderer path.</summary>
    public readonly struct SpriteAnimationKeyframe
        : global::System.IEquatable<SpriteAnimationKeyframe>
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

        /// <summary>Compares the stored fields with another value.</summary>
        public bool Equals(SpriteAnimationKeyframe other)
        {
            return Time.Equals(other.Time)
                && EqualityComparer<Sprite>.Default.Equals(Sprite, other.Sprite)
                && EqualityComparer<string>.Default.Equals(BindingPath, other.BindingPath);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is SpriteAnimationKeyframe other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return global::WallstopStudios.UnityHelpers.Core.Helper.Objects.HashCode(
                Time,
                Sprite,
                BindingPath
            );
        }
    }
}
