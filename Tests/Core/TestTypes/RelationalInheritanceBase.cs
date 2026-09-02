// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// A base class whose relational fields are all <c>private</c>, which the documentation
    /// recommends and which reflection keyed on the most derived type never returns.
    /// </summary>
    public abstract class RelationalInheritanceBase : MonoBehaviour
    {
        [SiblingComponent]
        private BoxCollider _sibling;

        [ChildComponent]
        private SpriteRenderer _child;

        [ParentComponent]
        private Rigidbody _parent;

        /// <summary>The sibling this base declared, or null when nothing bound it.</summary>
        public BoxCollider Sibling => _sibling;

        /// <summary>The child this base declared, or null when nothing bound it.</summary>
        public SpriteRenderer Child => _child;

        /// <summary>The parent this base declared, or null when nothing bound it.</summary>
        public Rigidbody Parent => _parent;
    }
}
