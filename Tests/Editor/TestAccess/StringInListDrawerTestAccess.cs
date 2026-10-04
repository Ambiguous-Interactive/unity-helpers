// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Base;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Utils;
    using static WallstopStudios.UnityHelpers.Editor.CustomDrawers.StringInListDrawer;

    /// <summary>Provides test-side setup and inspection for StringInListDrawer.</summary>
    internal static class StringInListDrawerTestAccess
    {
        /// <summary>Provides test-side access to OptionButtonMarginVertical.</summary>
        public static int OptionButtonMarginVertical =>
            PopupStyles.OptionButton.margin?.vertical ?? 0;

        /// <summary>Provides test-side access to OptionFooterPadding.</summary>
        public static float OptionFooterPadding => OptionBottomPadding;

        /// <summary>Provides test-side access to PaginationButtonHeight.</summary>
        public static float PaginationButtonHeight => PopupStyles.PaginationButtonLeft.fixedHeight;

        /// <summary>Provides test-side access to PopupWidthValue.</summary>
        public static float PopupWidthValue => PopupWidth;

        /// <summary>Provides test-side access to EmptySearchHorizontalPaddingValue.</summary>
        public static float EmptySearchHorizontalPaddingValue => EmptySearchHorizontalPadding;

        /// <summary>Provides test-side access to EmptyResultsMessageValue.</summary>
        public static string EmptyResultsMessageValue => EmptyResultsMessage;

        /// <summary>Provides test-side access to EmptySearchExtraPaddingValue.</summary>
        public static float EmptySearchExtraPaddingValue => EmptySearchExtraPadding;

        /// <summary>Provides test-side access to CalculatePopupTargetHeight.</summary>
        /// <param name="rowsOnPage">The rows on page used by the test.</param>
        /// <param name="includePagination">The include pagination used by the test.</param>
        /// <returns>The result of the test-side operation.</returns>
        public static float CalculatePopupTargetHeight(int rowsOnPage, bool includePagination)
        {
            return StringInListDrawer.CalculatePopupTargetHeight(rowsOnPage, includePagination);
        }

        /// <summary>Provides test-side access to CalculatePopupChromeHeight.</summary>
        /// <param name="includePagination">The include pagination used by the test.</param>
        /// <returns>The result of the test-side operation.</returns>
        public static float CalculatePopupChromeHeight(bool includePagination)
        {
            return StringInListDrawer.CalculatePopupChromeHeight(includePagination);
        }

        /// <summary>Provides test-side access to GetOptionRowHeight.</summary>
        /// <returns>The result of the test-side operation.</returns>
        public static float GetOptionRowHeight()
        {
            return StringInListDrawer.GetOptionRowHeight();
        }

        /// <summary>Provides test-side access to GetOptionControlHeight.</summary>
        /// <returns>The result of the test-side operation.</returns>
        public static float GetOptionControlHeight()
        {
            return StringInListDrawer.GetOptionControlHeight();
        }

        /// <summary>Provides test-side access to CalculateEmptySearchHeight.</summary>
        /// <returns>The result of the test-side operation.</returns>
        public static float CalculateEmptySearchHeight()
        {
            return StringInListDrawer.CalculateEmptySearchHeight();
        }

        /// <summary>Provides test-side access to CalculateEmptySearchHeightWithMeasurement.</summary>
        /// <param name="measuredHelpHeight">The measured help height used by the test.</param>
        /// <returns>The result of the test-side operation.</returns>
        public static float CalculateEmptySearchHeightWithMeasurement(float measuredHelpHeight)
        {
            return StringInListDrawer.CalculateEmptySearchHeight(measuredHelpHeight);
        }

        /// <summary>Provides test-side access to CalculateRowsOnPage.</summary>
        /// <param name="filteredCount">The filtered count used by the test.</param>
        /// <param name="pageSize">The page size used by the test.</param>
        /// <param name="currentPage">The current page used by the test.</param>
        /// <returns>The result of the test-side operation.</returns>
        public static int CalculateRowsOnPage(int filteredCount, int pageSize, int currentPage)
        {
            return StringInListDrawer.CalculateRowsOnPage(filteredCount, pageSize, currentPage);
        }
    }
#endif
}
