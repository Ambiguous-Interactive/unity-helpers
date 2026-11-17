// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using Core.Attributes;
    using UnityEngine;
    using UnityEngine.Serialization;

    [DisallowMultipleComponent]
    public sealed class SpriteRendererSync : MonoBehaviour
    {
        public int? DynamicSortingOrderOverride { get; set; }

        public SpriteRenderer DynamicToMatch
        {
            get => GetDynamicSpriteRenderer();
            set => _cachedSpriteRenderer = value;
        }

        [FormerlySerializedAs("_toMatch")]
        public SpriteRenderer toMatch;

        [FormerlySerializedAs("_matchColor")]
        public bool matchColor;

        [FormerlySerializedAs("_matchMaterial")]
        public bool matchMaterial;

        public bool matchSortingLayer = true;

        public bool matchOrderInLayer = true;

        public Func<SpriteRenderer> dynamicToMatch;

        [SerializeField]
        [SiblingComponent]
        private SpriteRenderer _spriteRenderer;

        private SpriteRenderer _cachedSpriteRenderer;

        private void Awake()
        {
            if (_spriteRenderer == null)
            {
                this.AssignSiblingComponents();
            }
        }

        private void LateUpdate()
        {
            SpriteRenderer localToMatch =
                dynamicToMatch != null ? GetDynamicSpriteRenderer() : toMatch;
            if (localToMatch == null)
            {
                _spriteRenderer.sprite = null;
                return;
            }

            _spriteRenderer.sprite = localToMatch.sprite;
            _spriteRenderer.enabled = localToMatch.enabled;
            _spriteRenderer.flipX = localToMatch.flipX;
            _spriteRenderer.flipY = localToMatch.flipY;
            if (matchColor)
            {
                _spriteRenderer.color = localToMatch.color;
            }

            if (matchMaterial)
            {
                _spriteRenderer.material = localToMatch.material;
            }

            if (matchSortingLayer)
            {
                _spriteRenderer.sortingLayerName = localToMatch.sortingLayerName;
            }

            if (matchOrderInLayer)
            {
                _spriteRenderer.sortingOrder = DynamicSortingOrderOverride
                    ?? localToMatch.sortingOrder;
            }

            _spriteRenderer.size = localToMatch.size;
            _spriteRenderer.spriteSortPoint = localToMatch.spriteSortPoint;
            _spriteRenderer.drawMode = localToMatch.drawMode;
            _spriteRenderer.tileMode = localToMatch.tileMode;
        }

        private SpriteRenderer GetDynamicSpriteRenderer()
        {
            if (_cachedSpriteRenderer != null && _cachedSpriteRenderer.gameObject.activeSelf)
            {
                return _cachedSpriteRenderer;
            }

            _cachedSpriteRenderer = dynamicToMatch();
            return _cachedSpriteRenderer;
        }
    }
}
