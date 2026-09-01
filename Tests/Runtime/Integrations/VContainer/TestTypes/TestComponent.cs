// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if VCONTAINER_PRESENT
namespace WallstopStudios.UnityHelpers.Tests.Integrations.VContainer.Runtime
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class TestComponent : MonoBehaviour
    {
        [ParentComponent(OnlyAncestors = true)]
        public Rigidbody parentBody;

        [ChildComponent(OnlyDescendants = true)]
        public CapsuleCollider childCollider;
    }
}
#endif
