// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils.WButton
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;
    using static WallstopStudios.UnityHelpers.Editor.Utils.WButton.WButtonStyles;

    /// <summary>Provides test-side setup and inspection for WButtonStyles.</summary>
    internal static class WButtonStylesTestAccess
    {
        /// <summary>
        /// Gets the number of colored button styles currently retained, for testing.
        /// </summary>
        internal static int ColoredButtonStyleCount => ColoredButtonStyles.Count;

        /// <summary>
        /// Gets the number of colored mini button styles currently retained, for testing.
        /// </summary>
        internal static int ColoredMiniButtonStyleCount => ColoredMiniButtonStyles.Count;

        /// <summary>
        /// Gets the bound both colored style caches evict at.
        /// </summary>
        internal static int MaxColoredButtonStyleCount => MaxColoredButtonStyles;

        /// <summary>
        /// Drops every cached colored style, destroying the textures each owns.
        /// </summary>
        internal static void ClearColoredStyleCaches()
        {
            ColoredButtonStyles.Clear();
            ColoredMiniButtonStyles.Clear();
        }
    }
#endif
}
