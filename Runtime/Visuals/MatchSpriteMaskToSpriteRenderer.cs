// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Visuals
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>Keeps a SpriteMask's sprite matched to its sibling SpriteRenderer.</summary>
    /// <remarks>Only the sprite is synchronized; sorting ranges, transforms, and mask interaction remain caller-owned.</remarks>
    [RequireComponent(typeof(SpriteMask), typeof(SpriteRenderer))]
    [DisallowMultipleComponent]
    [AddComponentMenu("Wallstop Studios/Unity Helpers/Match Sprite Mask To Sprite Renderer")]
    public sealed class MatchSpriteMaskToSpriteRenderer : MonoBehaviour
    {
        [SiblingComponent]
        private SpriteMask _mask;

        [SiblingComponent]
        private SpriteRenderer _renderer;

        /// <summary>Copies the current sprite immediately, resolving missing sibling references as needed.</summary>
        public void Synchronize()
        {
            if (_mask == null || _renderer == null)
            {
                this.AssignRelationalComponents();
            }

            if (_mask != null && _renderer != null && _mask.sprite != _renderer.sprite)
            {
                _mask.sprite = _renderer.sprite;
            }
        }

        private void OnEnable()
        {
            Synchronize();
        }

        private void LateUpdate()
        {
            Synchronize();
        }
    }
}
