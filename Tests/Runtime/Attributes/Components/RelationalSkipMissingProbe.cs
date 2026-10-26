// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes.Components
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    public sealed class RelationalSkipMissingProbe : MonoBehaviour
    {
        [ParentComponent(OnlyAncestors = true, SkipIfAssigned = true, Optional = true)]
        public SpriteRenderer parent;

        [SiblingComponent(SkipIfAssigned = true, Optional = true)]
        public SpriteRenderer sibling;

        [ChildComponent(OnlyDescendants = true, SkipIfAssigned = true, Optional = true)]
        public SpriteRenderer child;
    }
}
