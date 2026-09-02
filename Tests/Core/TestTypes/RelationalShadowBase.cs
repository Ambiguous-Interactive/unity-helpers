// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Declares a relational field a subclass hides with <c>new</c>.
    /// </summary>
    public abstract class RelationalShadowBase : MonoBehaviour
    {
        [SiblingComponent(Optional = true)]
        protected BoxCollider _collider;

        /// <summary>The field this base declared, read through the base's own name binding.</summary>
        public BoxCollider BaseCollider => _collider;
    }
}
