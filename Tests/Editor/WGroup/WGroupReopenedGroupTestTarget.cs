// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.WGroup
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for re-opening an earlier-declared group while a later group is still open.
    /// Auto-include must follow the most recently encountered <see cref="WGroupAttribute"/>.
    /// </summary>
    public sealed class WGroupReopenedGroupTestTarget : ScriptableObject
    {
        /// <summary>
        /// Anchor of the first group.
        /// </summary>
        [WGroup("Alpha", autoIncludeCount: WGroupAttribute.InfiniteAutoInclude)]
        public int alphaExplicit;

        /// <summary>
        /// Captured by Alpha, which is the group most recently encountered above it.
        /// </summary>
        public int alphaAuto;

        /// <summary>
        /// Anchor of the second group, declared after Alpha.
        /// </summary>
        [WGroup("Beta", autoIncludeCount: WGroupAttribute.InfiniteAutoInclude)]
        public int betaExplicit;

        /// <summary>
        /// Captured by Beta.
        /// </summary>
        public int betaAuto;

        /// <summary>
        /// Re-opens Alpha while Beta is still open.
        /// </summary>
        [WGroup("Alpha", autoIncludeCount: WGroupAttribute.InfiniteAutoInclude)]
        public int alphaReopened;

        /// <summary>
        /// Must be captured by Alpha, the group written directly above it, rather than by Beta.
        /// </summary>
        public int alphaAfterReopen;
    }
}
#endif
