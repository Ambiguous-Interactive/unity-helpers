// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Integrations.VContainer
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    public sealed class Consumer : MonoBehaviour
    {
        public SpriteRenderer SR => _spriteRenderer;

        [SiblingComponent]
        public SpriteRenderer _spriteRenderer;
    }
}
