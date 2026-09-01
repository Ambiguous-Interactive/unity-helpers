// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if VCONTAINER_PRESENT
namespace WallstopStudios.UnityHelpers.Tests.Integrations.VContainer
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal class BaseWithSibling : MonoBehaviour
    {
        [SiblingComponent]
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
        protected internal SpriteRenderer _spriteRenderer;
#pragma warning restore CS0649 // Field is never assigned to, and will always have its default value

        public SpriteRenderer SR => _spriteRenderer;
    }
}
#endif
