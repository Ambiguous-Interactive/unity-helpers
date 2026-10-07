// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace Samples.UnityHelpers.Relational.Basic
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Minimal, container-free example of Relational Component Attributes.
    /// Attach to a child GameObject with a parent and a sibling to see fields auto-assigned.
    /// </summary>
    public sealed class RelationalBasicConsumer : MonoBehaviour
    {
        [SiblingComponent]
        private Transform _siblingTransform;

        [ChildComponent]
        private Collider _childCollider;

        [ParentComponent(OnlyAncestors = true, MaxDepth = 1)]
        private Transform _directParent;

        private void Awake()
        {
            this.AssignRelationalComponents();
        }

        private void Start()
        {
            string parentName = _directParent != null ? _directParent.name : "<none>";
            string siblingName = _siblingTransform != null ? _siblingTransform.name : "<none>";
            string childName = _childCollider != null ? _childCollider.name : "<none>";
            Debug.Log(
                $"Relational assigned → parent={parentName}, sibling={siblingName}, child={childName}",
                this
            );
        }
    }
}
