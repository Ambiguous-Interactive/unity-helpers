// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if REFLEX_PRESENT
namespace WallstopStudios.UnityHelpers.Tests.Integrations.Reflex.Runtime
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class ReflexRelationalTester : MonoBehaviour
    {
        [ParentComponent(OnlyAncestors = true)]
        public Rigidbody parentBody;

        [ChildComponent(OnlyDescendants = true)]
        public CapsuleCollider childCollider;
    }
}
#endif
