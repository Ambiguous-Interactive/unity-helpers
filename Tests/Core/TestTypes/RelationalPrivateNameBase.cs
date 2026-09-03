// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Declares a private relational field whose name a subclass reuses. C# does not treat that as
    /// hiding -- a private field is invisible to a derived type -- so both are real fields and both
    /// must be bound.
    /// </summary>
    public abstract class RelationalPrivateNameBase : MonoBehaviour
    {
        [SiblingComponent]
        private BoxCollider _collider;

        /// <summary>The field this base declared.</summary>
        public BoxCollider BaseCollider => _collider;
    }
}
