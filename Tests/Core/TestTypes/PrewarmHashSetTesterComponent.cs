// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestTypes
{
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Carries a set-valued relational field, whose clearer the prewarm used to leave cold, and is
    /// used by nothing else so a "not cached before" assertion means what it says.
    /// </summary>
    public sealed class PrewarmHashSetTesterComponent : MonoBehaviour
    {
        [SiblingComponent]
        public HashSet<BoxCollider> siblingColliders;
    }
}
