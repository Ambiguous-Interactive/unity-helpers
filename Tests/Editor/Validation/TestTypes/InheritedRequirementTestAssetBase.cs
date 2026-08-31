// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation.TestTypes
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Declares a required slot that only a derived type is ever authored with.
    /// </summary>
    /// <remarks>
    /// An asset names the script of the type that was authored, not the one that declared the
    /// field, so a check that registered the annotation under its declaring type alone would never
    /// look at any document carrying it.
    /// </remarks>
    internal abstract class InheritedRequirementTestAssetBase : ScriptableObject
    {
        /// <summary>A required reference declared here and authored on a derived type.</summary>
        [WNotNull]
        public Material inheritedMaterial;
    }
}
