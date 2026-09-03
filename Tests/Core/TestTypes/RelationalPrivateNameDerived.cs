// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Reuses the base's private field name without <c>new</c>, which compiles cleanly precisely
    /// because nothing is being hidden.
    /// </summary>
    public sealed class RelationalPrivateNameDerived : RelationalPrivateNameBase
    {
        [SiblingComponent]
        private BoxCollider _collider;

        /// <summary>The field this subclass declared.</summary>
        public BoxCollider DerivedCollider => _collider;
    }
}
