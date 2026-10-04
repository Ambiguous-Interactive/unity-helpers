// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Base;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Utils;
    using static WallstopStudios.UnityHelpers.Editor.CustomDrawers.WValueDropDownDrawer;

    /// <summary>Provides test-side setup and inspection for WValueDropDownDrawer.</summary>
    internal static class WValueDropDownDrawerTestAccess
    {
        /// <summary>
        /// Gets the number of display-label sets currently retained, for testing.
        /// </summary>
        public static int DisplayLabelsCacheCount => DisplayLabelsCaches.Count;

        /// <summary>
        /// Gets the bound the display-label cache evicts at, for testing.
        /// </summary>
        public static int MaxDisplayLabelsCacheCount => MaxDisplayLabelsCacheEntries;

        /// <summary>
        /// Gets the number of formatted option labels currently retained, for testing.
        /// </summary>
        public static int FormattedOptionCacheCount => FormattedOptionCache.Count;

        /// <summary>
        /// Gets the bound the formatted option cache evicts at, for testing.
        /// </summary>
        public static int MaxFormattedOptionCacheCount => MaxFormattedOptionCacheEntries;

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

        /// <summary>
        /// Reads the cached display labels for a property path, populating them when absent.
        /// </summary>
        public static string[] GetOrCreateDisplayLabels(string cacheKey, object[] options)
        {
            return WValueDropDownDrawer.GetOrCreateDisplayLabels(cacheKey, options);
        }

        /// <summary>
        /// Drops every cached display-label set and formatted option label, for testing.
        /// </summary>
        public static void ClearCaches()
        {
            DisplayLabelsCaches.Clear();
            FormattedOptionCache.Clear();
        }

        /// <summary>Provides test-side access to CalculatePopupTargetHeight.</summary>
        /// <param name="rowsOnPage">The rows on page used by the test.</param>
        /// <param name="includePagination">The include pagination used by the test.</param>
        /// <returns>The result of the test-side operation.</returns>
        public static float CalculatePopupTargetHeight(int rowsOnPage, bool includePagination)
        {
            return WValueDropDownDrawer.CalculatePopupTargetHeight(rowsOnPage, includePagination);
        }

        /// <summary>Provides test-side access to CalculatePopupChromeHeight.</summary>
        /// <param name="includePagination">The include pagination used by the test.</param>
        /// <returns>The result of the test-side operation.</returns>
        public static float CalculatePopupChromeHeight(bool includePagination)
        {
            return WValueDropDownDrawer.CalculatePopupChromeHeight(includePagination);
        }

        /// <summary>Provides test-side access to GetOptionRowHeight.</summary>
        /// <returns>The result of the test-side operation.</returns>
        public static float GetOptionRowHeight()
        {
            return WValueDropDownDrawer.GetOptionRowHeight();
        }

        /// <summary>Provides test-side access to GetOptionControlHeight.</summary>
        /// <returns>The result of the test-side operation.</returns>
        public static float GetOptionControlHeight()
        {
            return WValueDropDownDrawer.GetOptionControlHeight();
        }

        /// <summary>Provides test-side access to CalculateEmptySearchHeight.</summary>
        /// <returns>The result of the test-side operation.</returns>
        public static float CalculateEmptySearchHeight()
        {
            return WValueDropDownDrawer.CalculateEmptySearchHeight();
        }

        /// <summary>Provides test-side access to CalculateEmptySearchHeightWithMeasurement.</summary>
        /// <param name="measuredHelpHeight">The measured help height used by the test.</param>
        /// <returns>The result of the test-side operation.</returns>
        public static float CalculateEmptySearchHeightWithMeasurement(float measuredHelpHeight)
        {
            return WValueDropDownDrawer.CalculateEmptySearchHeight(measuredHelpHeight);
        }

        /// <summary>Provides test-side access to CalculateRowsOnPage.</summary>
        /// <param name="filteredCount">The filtered count used by the test.</param>
        /// <param name="pageSize">The page size used by the test.</param>
        /// <param name="currentPage">The current page used by the test.</param>
        /// <returns>The result of the test-side operation.</returns>
        public static int CalculateRowsOnPage(int filteredCount, int pageSize, int currentPage)
        {
            return WValueDropDownDrawer.CalculateRowsOnPage(filteredCount, pageSize, currentPage);
        }

        /// <summary>Provides test-side access to ResolveSelectedIndex.</summary>
        /// <param name="property">The property used by the test.</param>
        /// <param name="valueType">The value type used by the test.</param>
        /// <param name="options">The options used by the test.</param>
        /// <returns>The result of the test-side operation.</returns>
        public static int ResolveSelectedIndex(
            SerializedProperty property,
            Type valueType,
            object[] options
        )
        {
            return WValueDropDownDrawer.ResolveSelectedIndex(property, valueType, options);
        }

        /// <summary>Provides test-side access to MatchesAuthoredOption.</summary>
        /// <param name="serializedValue">The serialized value used by the test.</param>
        /// <param name="option">The option used by the test.</param>
        /// <returns>The result of the test-side operation.</returns>
        public static bool MatchesAuthoredOption(object serializedValue, object option)
        {
            return WValueDropDownDrawer.MatchesAuthoredOption(serializedValue, option);
        }

        /// <summary>Provides test-side access to FormatOptionCached.</summary>
        /// <param name="option">The option used by the test.</param>
        /// <returns>The result of the test-side operation.</returns>
        public static string FormatOptionCached(object option)
        {
            return WValueDropDownDrawer.FormatOptionCached(option);
        }

        /// <summary>Provides test-side access to BuildDisplayLabelsUncached.</summary>
        /// <param name="options">The options used by the test.</param>
        /// <returns>The result of the test-side operation.</returns>
        public static string[] BuildDisplayLabelsUncached(object[] options)
        {
            return WValueDropDownDrawer.BuildDisplayLabelsUncached(options);
        }
    }
#endif
}
