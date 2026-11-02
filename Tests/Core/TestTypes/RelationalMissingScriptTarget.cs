// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    public sealed class RelationalMissingScriptTarget : MonoBehaviour
    {
        [SiblingComponent(Optional = true)]
        public Component[] siblingAllArray;

        [SiblingComponent(Optional = true)]
        public List<Component> siblingAllList;

        [SiblingComponent(Optional = true)]
        public HashSet<Component> siblingAllSet;

        [SiblingComponent(Optional = true)]
        public MonoBehaviour[] siblingBehaviourArray;

        [SiblingComponent(Optional = true)]
        public List<MonoBehaviour> siblingBehaviourList;

        [SiblingComponent(Optional = true)]
        public HashSet<MonoBehaviour> siblingBehaviourSet;

        [SiblingComponent(Optional = true, NameFilter = "")]
        public Component[] siblingFilteredArray;

        [SiblingComponent(Optional = true, NameFilter = "")]
        public List<Component> siblingFilteredList;

        [SiblingComponent(Optional = true, NameFilter = "")]
        public HashSet<Component> siblingFilteredSet;

        [SiblingComponent(Optional = true, MaxCount = 2)]
        public Component[] siblingBoundedArray;

        [SiblingComponent(Optional = true, MaxCount = 2)]
        public List<Component> siblingBoundedList;

        [SiblingComponent(Optional = true, MaxCount = 2)]
        public HashSet<Component> siblingBoundedSet;

        [SiblingComponent(Optional = true, NameFilter = "", MaxCount = 2)]
        public Component[] siblingFilteredBoundedArray;

        [SiblingComponent(Optional = true, NameFilter = "", MaxCount = 2)]
        public List<Component> siblingFilteredBoundedList;

        [SiblingComponent(Optional = true, NameFilter = "", MaxCount = 2)]
        public HashSet<Component> siblingFilteredBoundedSet;

        [ParentComponent(Optional = true, OnlyAncestors = true)]
        public Component[] parentAllArray;

        [ParentComponent(Optional = true, OnlyAncestors = true)]
        public List<Component> parentAllList;

        [ParentComponent(Optional = true, OnlyAncestors = true)]
        public HashSet<Component> parentAllSet;

        [ParentComponent(Optional = true, OnlyAncestors = true)]
        public MonoBehaviour[] parentBehaviourArray;

        [ParentComponent(Optional = true, OnlyAncestors = true)]
        public List<MonoBehaviour> parentBehaviourList;

        [ParentComponent(Optional = true, OnlyAncestors = true)]
        public HashSet<MonoBehaviour> parentBehaviourSet;

        [ParentComponent(Optional = true, OnlyAncestors = true, NameFilter = "")]
        public Component[] parentFilteredArray;

        [ParentComponent(Optional = true, OnlyAncestors = true, NameFilter = "")]
        public List<Component> parentFilteredList;

        [ParentComponent(Optional = true, OnlyAncestors = true, NameFilter = "")]
        public HashSet<Component> parentFilteredSet;

        [ParentComponent(Optional = true, OnlyAncestors = true, MaxCount = 2)]
        public Component[] parentBoundedArray;

        [ParentComponent(Optional = true, OnlyAncestors = true, MaxCount = 2)]
        public List<Component> parentBoundedList;

        [ParentComponent(Optional = true, OnlyAncestors = true, MaxCount = 2)]
        public HashSet<Component> parentBoundedSet;

        [ParentComponent(Optional = true, OnlyAncestors = true, NameFilter = "", MaxCount = 2)]
        public Component[] parentFilteredBoundedArray;

        [ParentComponent(Optional = true, OnlyAncestors = true, NameFilter = "", MaxCount = 2)]
        public List<Component> parentFilteredBoundedList;

        [ParentComponent(Optional = true, OnlyAncestors = true, NameFilter = "", MaxCount = 2)]
        public HashSet<Component> parentFilteredBoundedSet;

        [ParentComponent(Optional = true, OnlyAncestors = true, MaxDepth = 1)]
        public Component[] parentDepthLimitedArray;

        [ParentComponent(Optional = true, OnlyAncestors = true, MaxDepth = 1)]
        public List<Component> parentDepthLimitedList;

        [ParentComponent(Optional = true, OnlyAncestors = true, MaxDepth = 1)]
        public HashSet<Component> parentDepthLimitedSet;

        [ChildComponent(Optional = true, OnlyDescendants = true)]
        public Component[] childAllArray;

        [ChildComponent(Optional = true, OnlyDescendants = true)]
        public List<Component> childAllList;

        [ChildComponent(Optional = true, OnlyDescendants = true)]
        public HashSet<Component> childAllSet;

        [ChildComponent(Optional = true, OnlyDescendants = true)]
        public MonoBehaviour[] childBehaviourArray;

        [ChildComponent(Optional = true, OnlyDescendants = true)]
        public List<MonoBehaviour> childBehaviourList;

        [ChildComponent(Optional = true, OnlyDescendants = true)]
        public HashSet<MonoBehaviour> childBehaviourSet;

        [ChildComponent(Optional = true, OnlyDescendants = true, NameFilter = "")]
        public Component[] childFilteredArray;

        [ChildComponent(Optional = true, OnlyDescendants = true, NameFilter = "")]
        public List<Component> childFilteredList;

        [ChildComponent(Optional = true, OnlyDescendants = true, NameFilter = "")]
        public HashSet<Component> childFilteredSet;

        [ChildComponent(Optional = true, OnlyDescendants = true, MaxCount = 2)]
        public Component[] childBoundedArray;

        [ChildComponent(Optional = true, OnlyDescendants = true, MaxCount = 2)]
        public List<Component> childBoundedList;

        [ChildComponent(Optional = true, OnlyDescendants = true, MaxCount = 2)]
        public HashSet<Component> childBoundedSet;

        [ChildComponent(Optional = true, OnlyDescendants = true, NameFilter = "", MaxCount = 2)]
        public Component[] childFilteredBoundedArray;

        [ChildComponent(Optional = true, OnlyDescendants = true, NameFilter = "", MaxCount = 2)]
        public List<Component> childFilteredBoundedList;

        [ChildComponent(Optional = true, OnlyDescendants = true, NameFilter = "", MaxCount = 2)]
        public HashSet<Component> childFilteredBoundedSet;
    }
}
