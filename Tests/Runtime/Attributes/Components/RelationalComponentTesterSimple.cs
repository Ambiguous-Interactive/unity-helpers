// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes.Components
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    [RequireComponent(typeof(BoxCollider2D))]
    [RequireComponent(typeof(BoxCollider2D))]
    [RequireComponent(typeof(PolygonCollider2D))]
    public sealed class RelationalComponentTesterSimple : MonoBehaviour
    {
        [SiblingComponent]
        internal SpriteRenderer _spriteRenderer;

        [SiblingComponent]
        internal Transform _transform;

        [SiblingComponent]
        internal PolygonCollider2D _polygonCollider;

        [SiblingComponent]
        internal BoxCollider2D _boxCollider;
    }
}
