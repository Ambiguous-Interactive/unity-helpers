// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using UnityEngine;

    /// <summary>
    /// Reuses <see cref="RelationalPrivateNameBase"/>'s private field name with NO relational
    /// attribute, so the cache holds one entry for a name that two live fields answer to.
    /// </summary>
    public sealed class RelationalPrivateNameUnrelatedDerived : RelationalPrivateNameBase
    {
        [SerializeField]
        private BoxCollider _collider;

        /// <summary>The unattributed field, which nothing should assign.</summary>
        public BoxCollider DerivedCollider => _collider;
    }
}
