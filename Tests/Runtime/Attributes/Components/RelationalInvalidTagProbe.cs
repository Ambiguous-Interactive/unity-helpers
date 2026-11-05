// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class RelationalInvalidTagProbe : MonoBehaviour
    {
        internal const string MissingTag = nameof(RelationalInvalidTagProbe);

        [ParentComponent(OnlyAncestors = true, TagFilter = MissingTag, Optional = true)]
        public Transform invalidParent;

        [ParentComponent(OnlyAncestors = true, TagFilter = MissingTag, Optional = true)]
        public Transform[] invalidParents;

        [ParentComponent(OnlyAncestors = true, TagFilter = MissingTag, Optional = true)]
        public List<Transform> invalidParentList;

        [ParentComponent(OnlyAncestors = true, TagFilter = MissingTag, Optional = true)]
        public HashSet<Transform> invalidParentSet;

        [ParentComponent(OnlyAncestors = true)]
        public Transform parent;

        [ParentComponent(OnlyAncestors = true, TagFilter = MissingTag)]
        public Transform requiredParent;

        [SiblingComponent(TagFilter = MissingTag, Optional = true)]
        public Transform invalidSibling;

        [SiblingComponent(TagFilter = MissingTag, Optional = true)]
        public Transform[] invalidSiblings;

        [SiblingComponent(TagFilter = MissingTag, Optional = true)]
        public List<Transform> invalidSiblingList;

        [SiblingComponent(TagFilter = MissingTag, Optional = true)]
        public HashSet<Transform> invalidSiblingSet;

        [SiblingComponent(TagFilter = "", Optional = true)]
        public Transform emptyTag;

        [SiblingComponent(TagFilter = " ", Optional = true)]
        public Transform whitespaceTag;

        [SiblingComponent(TagFilter = "Untagged")]
        public Transform sibling;

        [SiblingComponent(TagFilter = MissingTag)]
        public Transform requiredSibling;

        [ChildComponent(OnlyDescendants = true, TagFilter = MissingTag, Optional = true)]
        public Transform invalidChild;

        [ChildComponent(OnlyDescendants = true, TagFilter = MissingTag, Optional = true)]
        public Transform[] invalidChildren;

        [ChildComponent(OnlyDescendants = true, TagFilter = MissingTag, Optional = true)]
        public List<Transform> invalidChildList;

        [ChildComponent(OnlyDescendants = true, TagFilter = MissingTag, Optional = true)]
        public HashSet<Transform> invalidChildSet;

        [ChildComponent(OnlyDescendants = true)]
        public Transform child;

        [ChildComponent(OnlyDescendants = true, TagFilter = MissingTag)]
        public Transform requiredChild;
    }
}
