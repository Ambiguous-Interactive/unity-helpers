// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /*
        A SEALED element type, which is the only shape that reaches the single-field fast path in
        ChildComponentAttribute. ChildDisabledBehaviourTester uses BoxCollider, and a non-sealed
        type routes to the fallback instead -- which is why the disabled-component filter could be
        missing from the fast path with every existing test green.
    */
    public sealed class ChildSealedDisabledTester : MonoBehaviour
    {
        [ChildComponent(OnlyDescendants = true, IncludeInactive = false)]
        public TransformProbe activeOnly;

        [ChildComponent(OnlyDescendants = true, IncludeInactive = false)]
        public TransformProbe[] activeOnlyArray;

        [ChildComponent(OnlyDescendants = true, IncludeInactive = true)]
        public TransformProbe includeInactive;
    }
}
