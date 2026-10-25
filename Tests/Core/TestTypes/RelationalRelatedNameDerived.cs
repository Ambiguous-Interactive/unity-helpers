// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    public sealed class RelationalRelatedNameDerived : RelationalRelatedNameBase
    {
        public BoxCollider DerivedSiblingSingle => _siblingSingle;

        public BoxCollider[] DerivedSiblingArray => _siblingArray;

        public List<BoxCollider> DerivedSiblingList => _siblingList;

        public HashSet<BoxCollider> DerivedSiblingSet => _siblingSet;

        public BoxCollider DerivedParentSingle => _parentSingle;

        public BoxCollider[] DerivedParentArray => _parentArray;

        public List<BoxCollider> DerivedParentList => _parentList;

        public HashSet<BoxCollider> DerivedParentSet => _parentSet;

        public BoxCollider DerivedChildSingle => _childSingle;

        public BoxCollider[] DerivedChildArray => _childArray;

        public List<BoxCollider> DerivedChildList => _childList;

        public HashSet<BoxCollider> DerivedChildSet => _childSet;

        [SiblingComponent(Optional = true)]
        private BoxCollider _siblingSingle;

        [SiblingComponent(Optional = true)]
        private BoxCollider[] _siblingArray;

        [SiblingComponent(Optional = true)]
        private List<BoxCollider> _siblingList;

        [SiblingComponent(Optional = true)]
        private HashSet<BoxCollider> _siblingSet;

        [ParentComponent(Optional = true, OnlyAncestors = true)]
        private BoxCollider _parentSingle;

        [ParentComponent(Optional = true, OnlyAncestors = true)]
        private BoxCollider[] _parentArray;

        [ParentComponent(Optional = true, OnlyAncestors = true)]
        private List<BoxCollider> _parentList;

        [ParentComponent(Optional = true, OnlyAncestors = true)]
        private HashSet<BoxCollider> _parentSet;

        [ChildComponent(Optional = true, OnlyDescendants = true)]
        private BoxCollider _childSingle;

        [ChildComponent(Optional = true, OnlyDescendants = true)]
        private BoxCollider[] _childArray;

        [ChildComponent(Optional = true, OnlyDescendants = true)]
        private List<BoxCollider> _childList;

        [ChildComponent(Optional = true, OnlyDescendants = true)]
        private HashSet<BoxCollider> _childSet;
    }
}
