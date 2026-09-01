// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if ZENJECT_PRESENT
namespace WallstopStudios.UnityHelpers.Tests.Integrations.Zenject
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class Consumer : MonoBehaviour
    {
        [SiblingComponent]
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
        internal SpriteRenderer _spriteRenderer;
#pragma warning restore CS0649 // Field is never assigned to, and will always have its default value

        public SpriteRenderer SR => _spriteRenderer;
    }
}
#endif
