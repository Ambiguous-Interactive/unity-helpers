// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Single relational fields that must skip a disabled candidate and bind the enabled one behind
    /// it, for the concrete, interface and child shapes alike.
    /// </summary>
    public sealed class RelationalActiveOnlyTester : MonoBehaviour
    {
        [SiblingComponent(IncludeInactive = false)]
        public BoxCollider activeSibling;

        [SiblingComponent(IncludeInactive = false)]
        public ITestInterface activeSiblingInterface;

        [ChildComponent(IncludeInactive = false, OnlyDescendants = true)]
        public SphereCollider activeChild;

        // Deliberately asymmetric with the two above: for parents, IncludeInactive gates only
        // GameObject.activeInHierarchy, never per-component `enabled`. Asserted so the asymmetry
        // is a decision on record rather than something a future sweep "fixes".
        [ParentComponent(IncludeInactive = false, OnlyAncestors = true)]
        public CapsuleCollider firstParentEvenIfDisabled;
    }
}
