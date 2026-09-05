// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    public sealed class ChildMaxCountTester : MonoBehaviour
    {
        [ChildComponent(OnlyDescendants = true, MaxCount = 3)]
        public List<SpriteRenderer> limitedChildren;

        [ChildComponent(OnlyDescendants = true, MaxCount = 3)]
        public SpriteRenderer[] limitedChildrenArray;

        [ChildComponent(OnlyDescendants = true, MaxCount = 3)]
        public HashSet<SpriteRenderer> limitedChildrenSet;

        [ChildComponent(OnlyDescendants = true)]
        public List<SpriteRenderer> allChildren;
    }
}
