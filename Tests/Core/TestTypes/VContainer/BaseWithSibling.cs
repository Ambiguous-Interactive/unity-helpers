// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Integrations.VContainer
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    public class BaseWithSibling : MonoBehaviour
    {
        [SiblingComponent]
        public SpriteRenderer _spriteRenderer;

        public SpriteRenderer SR => _spriteRenderer;
    }
}
