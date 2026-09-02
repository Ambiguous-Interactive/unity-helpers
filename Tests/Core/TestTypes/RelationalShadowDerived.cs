// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Hides <see cref="RelationalShadowBase"/>'s relational field with <c>new</c>. C# name hiding
    /// says this declaration owns the name, and relational discovery follows it.
    /// </summary>
    public sealed class RelationalShadowDerived : RelationalShadowBase
    {
        [SiblingComponent(Optional = true)]
        private new BoxCollider _collider;

        /// <summary>The field this subclass declared.</summary>
        public BoxCollider DerivedCollider => _collider;
    }
}
