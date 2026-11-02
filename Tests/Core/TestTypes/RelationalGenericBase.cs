// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    public abstract class RelationalGenericBase<T> : MonoBehaviour
        where T : Component
    {
        [ParentComponent(OnlyAncestors = true)]
        public T parent;

        [ChildComponent(OnlyDescendants = true)]
        public List<T> children;

        [SiblingComponent]
        public T[] siblings;
    }
}
