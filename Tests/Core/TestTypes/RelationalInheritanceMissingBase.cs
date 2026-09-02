// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// A base class declaring a required private relational field that nothing can satisfy, so the
    /// missing-component error is observable rather than swallowed by never discovering the field.
    /// </summary>
    public abstract class RelationalInheritanceMissingBase : MonoBehaviour
    {
        [SiblingComponent]
        private Rigidbody2D _required;

        /// <summary>The unsatisfiable sibling, which must stay null.</summary>
        public Rigidbody2D Required => _required;
    }
}
