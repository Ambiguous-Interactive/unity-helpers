// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;
    using static WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils.DropDownShared;

    /// <summary>Provides test-side setup and inspection for DropDownShared.</summary>
    internal static class DropDownSharedTestAccess
    {
        /// <summary>
        /// Gets the number of formatted option labels currently retained, for testing.
        /// </summary>
        public static int FormattedOptionCacheCount => FormattedOptionCache.Count;

        /// <summary>
        /// Gets the option button margin vertical value for testing.
        /// </summary>
        public static int OptionButtonMarginVertical
        {
            get
            {
                EnsureStylesInitialized();
                return s_optionButton.margin?.vertical ?? 0;
            }
        }

        /// <summary>
        /// Gets the option footer padding for testing.
        /// </summary>
        public static float OptionFooterPadding => OptionBottomPadding;

        /// <summary>
        /// Gets the popup width value for testing.
        /// </summary>
        public static float PopupWidthValue => PopupWidth;

        /// <summary>
        /// Gets the empty search horizontal padding value for testing.
        /// </summary>
        public static float EmptySearchHorizontalPaddingValue => EmptySearchHorizontalPadding;

        /// <summary>
        /// Gets the empty results message value for testing.
        /// </summary>
        public static string EmptyResultsMessageValue => EmptyResultsMessage;

        /// <summary>
        /// Gets the empty search extra padding value for testing.
        /// </summary>
        public static float EmptySearchExtraPaddingValue => EmptySearchExtraPadding;
    }
#endif
}
