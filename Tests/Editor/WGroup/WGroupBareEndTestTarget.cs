// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.WGroup
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for a bare <see cref="WGroupEndAttribute"/> while two groups are open.
    /// The bare form closes every active group, so nothing after it is captured.
    /// </summary>
    public sealed class WGroupBareEndTestTarget : ScriptableObject
    {
        /// <summary>
        /// Anchor of the first group.
        /// </summary>
        [WGroup("Alpha", autoIncludeCount: WGroupAttribute.InfiniteAutoInclude)]
        public int alphaExplicit;

        /// <summary>
        /// Captured by Alpha.
        /// </summary>
        public int alphaAuto;

        /// <summary>
        /// Anchor of the second group, which stays open until the bare end below.
        /// </summary>
        [WGroup("Beta", autoIncludeCount: WGroupAttribute.InfiniteAutoInclude)]
        public int betaExplicit;

        /// <summary>
        /// Captured by Beta.
        /// </summary>
        public int betaAuto;

        /// <summary>
        /// Re-opens Alpha and immediately closes every open group. Belongs to Alpha, not Beta.
        /// </summary>
        [WGroup("Alpha", autoIncludeCount: WGroupAttribute.InfiniteAutoInclude)]
        [WGroupEnd]
        public int alphaClosing;

        /// <summary>
        /// Must be ungrouped: both Alpha and Beta closed above it.
        /// </summary>
        public int ungrouped;
    }
}
#endif
