// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /*
        A sealed component type reaches the single-field fast path; the non-sealed fixture exercises only the
        fallback.
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
