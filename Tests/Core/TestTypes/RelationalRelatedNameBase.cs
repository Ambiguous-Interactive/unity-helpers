// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    public abstract class RelationalRelatedNameBase : MonoBehaviour
    {
        internal static readonly string[] RelationalFieldNames =
        {
            nameof(_siblingSingle),
            nameof(_siblingArray),
            nameof(_siblingList),
            nameof(_siblingSet),
            nameof(_parentSingle),
            nameof(_parentArray),
            nameof(_parentList),
            nameof(_parentSet),
            nameof(_childSingle),
            nameof(_childArray),
            nameof(_childList),
            nameof(_childSet),
        };

        public Collider BaseSiblingSingle => _siblingSingle;

        public Collider[] BaseSiblingArray => _siblingArray;

        public List<Collider> BaseSiblingList => _siblingList;

        public HashSet<Collider> BaseSiblingSet => _siblingSet;

        public Collider BaseParentSingle => _parentSingle;

        public Collider[] BaseParentArray => _parentArray;

        public List<Collider> BaseParentList => _parentList;

        public HashSet<Collider> BaseParentSet => _parentSet;

        public Collider BaseChildSingle => _childSingle;

        public Collider[] BaseChildArray => _childArray;

        public List<Collider> BaseChildList => _childList;

        public HashSet<Collider> BaseChildSet => _childSet;

        [SiblingComponent(Optional = true)]
        private Collider _siblingSingle;

        [SiblingComponent(Optional = true)]
        private Collider[] _siblingArray;

        [SiblingComponent(Optional = true)]
        private List<Collider> _siblingList;

        [SiblingComponent(Optional = true)]
        private HashSet<Collider> _siblingSet;

        [ParentComponent(Optional = true, OnlyAncestors = true)]
        private Collider _parentSingle;

        [ParentComponent(Optional = true, OnlyAncestors = true)]
        private Collider[] _parentArray;

        [ParentComponent(Optional = true, OnlyAncestors = true)]
        private List<Collider> _parentList;

        [ParentComponent(Optional = true, OnlyAncestors = true)]
        private HashSet<Collider> _parentSet;

        [ChildComponent(Optional = true, OnlyDescendants = true)]
        private Collider _childSingle;

        [ChildComponent(Optional = true, OnlyDescendants = true)]
        private Collider[] _childArray;

        [ChildComponent(Optional = true, OnlyDescendants = true)]
        private List<Collider> _childList;

        [ChildComponent(Optional = true, OnlyDescendants = true)]
        private HashSet<Collider> _childSet;
    }
}
