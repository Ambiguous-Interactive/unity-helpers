// MIT License - Copyright (c) 2025 wallstop
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

    /// <summary>
    /// Provides shared constants, caching, and helper methods for dropdown drawer implementations.
    /// </summary>
    /// <remarks>
    /// This utility class consolidates common code used by IntDropDown, StringInList, and
    /// WValueDropDown drawers (both standard PropertyDrawer and Odin Inspector implementations).
    /// By centralizing these elements, we ensure consistent behavior and eliminate code duplication.
    /// </remarks>
    public static class DropDownShared
    {
        /// <summary>
        /// Width of pagination navigation buttons in pixels.
        /// </summary>
        public const float ButtonWidth = 24f;

        /// <summary>
        /// Width of the pagination label showing "Page X/Y" in pixels.
        /// </summary>
        public const float PageLabelWidth = 90f;

        /// <summary>
        /// Height of pagination buttons in pixels.
        /// </summary>
        public const float PaginationButtonHeight = 20f;

        /// <summary>
        /// Default width of popup windows in pixels.
        /// </summary>
        public const float PopupWidth = 360f;

        /// <summary>
        /// Bottom padding below option lists in pixels.
        /// </summary>
        public const float OptionBottomPadding = 6f;

        /// <summary>
        /// Extra height added to option rows in pixels.
        /// </summary>
        public const float OptionRowExtraHeight = 1.5f;

        /// <summary>
        /// Horizontal padding for empty search results area in pixels.
        /// </summary>
        public const float EmptySearchHorizontalPadding = 32f;

        /// <summary>
        /// Extra padding for empty search state in pixels.
        /// </summary>
        public const float EmptySearchExtraPadding = 12f;

        /// <summary>
        /// Message displayed when search yields no results.
        /// </summary>
        public const string EmptyResultsMessage = "No results match the current search.";

        /// <summary>
        /// Reusable GUIContent for empty search results message.
        /// </summary>
        public static readonly GUIContent EmptyResultsContent = new(EmptyResultsMessage);

        /// <summary>
        /// The number of distinct option values whose formatted label is retained.
        /// </summary>
        /// <remarks>
        /// The key is the option value itself, routinely a <c>UnityEngine.Object</c> supplied by a
        /// game's own dropdown source, so an unbounded cache roots every option ever rendered
        /// across scene changes and play sessions. Sized far above the options one dropdown can
        /// present so a single popup never evicts its own entries.
        /// </remarks>
        private const int MaxFormattedOptionCacheEntries = 2048;

        private static readonly Cache<object, string> FormattedOptionCache = CacheBuilder<
            object,
            string
        >
            .NewBuilder()
            .MaximumSize(MaxFormattedOptionCacheEntries)
            .InitialCapacity(16)
            .Build();
        private static readonly Dictionary<Type, string[]> EnumDisplayNameCache = new();
        private static readonly Dictionary<int, string> FallbackOptionLabelCache = new();

        private static float s_cachedOptionControlHeight = -1f;
        private static float s_cachedOptionRowHeight = -1f;
        private static GUIStyle s_optionButton;
        private static GUIStyle s_selectedOptionButton;
        private static GUIStyle s_paginationButtonLeft;
        private static GUIStyle s_paginationButtonRight;
        private static GUIStyle s_paginationLabel;

        /// <summary>
        /// Gets the cached style for option buttons.
        /// </summary>
        public static GUIStyle OptionButton
        {
            get
            {
                EnsureStylesInitialized();
                return s_optionButton;
            }
        }

        /// <summary>
        /// Gets the cached style for selected option buttons.
        /// </summary>
        public static GUIStyle SelectedOptionButton
        {
            get
            {
                EnsureStylesInitialized();
                return s_selectedOptionButton;
            }
        }

        /// <summary>
        /// Gets the cached style for left pagination buttons.
        /// </summary>
        public static GUIStyle PaginationButtonLeft
        {
            get
            {
                EnsureStylesInitialized();
                return s_paginationButtonLeft;
            }
        }

        /// <summary>
        /// Gets the cached style for right pagination buttons.
        /// </summary>
        public static GUIStyle PaginationButtonRight
        {
            get
            {
                EnsureStylesInitialized();
                return s_paginationButtonRight;
            }
        }

        /// <summary>
        /// Gets the cached style for pagination labels.
        /// </summary>
        public static GUIStyle PaginationLabel
        {
            get
            {
                EnsureStylesInitialized();
                return s_paginationLabel;
            }
        }

        /// <summary>
        /// Returns a cached string representation of an integer value.
        /// Delegates to <see cref="EditorCacheHelper.GetCachedIntString"/> for shared LRU caching.
        /// </summary>
        /// <param name="value">The integer to convert to string.</param>
        /// <returns>The cached string representation.</returns>
        public static string GetCachedIntString(int value)
        {
            return EditorCacheHelper.GetCachedIntString(value);
        }

        /// <summary>
        /// Returns a cached pagination label string in the format "Page X / Y".
        /// Delegates to <see cref="EditorCacheHelper.GetPaginationLabel"/> for shared LRU caching.
        /// </summary>
        /// <param name="currentPage">The current page number (1-based).</param>
        /// <param name="totalPages">The total number of pages.</param>
        /// <returns>The cached pagination label string.</returns>
        public static string GetPaginationLabel(int currentPage, int totalPages)
        {
            return EditorCacheHelper.GetPaginationLabel(currentPage, totalPages);
        }

        /// <summary>
        /// Returns a cached formatted string representation of an option value.
        /// Handles Unity objects, enums, and general objects appropriately.
        /// </summary>
        /// <param name="option">The option value to format.</param>
        /// <returns>The cached formatted string.</returns>
        public static string FormatOption(object option)
        {
            if (option == null)
            {
                return "(null)";
            }

            if (FormattedOptionCache.TryGet(option, out string cached))
            {
                return cached;
            }

            string formatted;
            if (option is int intValue)
            {
                formatted = GetCachedIntString(intValue);
            }
            else if (option is UnityEngine.Object unityObject)
            {
                if (unityObject == null)
                {
                    formatted = "(None)";
                }
                else
                {
                    string objectName = unityObject.name;
                    formatted = string.IsNullOrEmpty(objectName)
                        ? unityObject.GetType().Name
                        : objectName;
                }
            }
            else if (option is Enum enumValue)
            {
                formatted = ObjectNames.NicifyVariableName(enumValue.ToString());
            }
            else if (option is IFormattable formattable)
            {
                formatted = formattable.ToString(
                    null,
                    System.Globalization.CultureInfo.InvariantCulture
                );
            }
            else
            {
                formatted = option.ToString();
            }

            FormattedOptionCache.Set(option, formatted);
            return formatted;
        }

        /// <summary>
        /// Calculates the total number of pages needed for pagination.
        /// </summary>
        /// <param name="pageSize">The number of items per page.</param>
        /// <param name="filteredCount">The total number of filtered items.</param>
        /// <returns>The number of pages (minimum 1).</returns>
        public static int CalculatePageCount(int pageSize, int filteredCount)
        {
            if (filteredCount <= 0)
            {
                return 1;
            }

            return (filteredCount + pageSize - 1) / pageSize;
        }

        /// <summary>
        /// Calculates the number of rows displayed on a specific page.
        /// </summary>
        /// <param name="filteredCount">The total number of filtered items.</param>
        /// <param name="pageSize">The number of items per page.</param>
        /// <param name="currentPage">The current page index (0-based).</param>
        /// <returns>The number of rows on the page (minimum 1).</returns>
        public static int CalculateRowsOnPage(int filteredCount, int pageSize, int currentPage)
        {
            if (filteredCount <= 0 || pageSize <= 0)
            {
                return 1;
            }

            int maxPageIndex = CalculatePageCount(pageSize, filteredCount) - 1;
            int clampedPage = Mathf.Clamp(currentPage, 0, Mathf.Max(0, maxPageIndex));
            int startIndex = clampedPage * pageSize;
            int remaining = filteredCount - startIndex;
            if (remaining <= 0)
            {
                return 1;
            }

            return Mathf.Min(pageSize, remaining);
        }

        /// <summary>
        /// Calculates the target height for popup windows.
        /// </summary>
        /// <param name="rowsOnPage">The number of option rows displayed.</param>
        /// <param name="includePagination">Whether pagination controls are visible.</param>
        /// <returns>The target height in pixels.</returns>
        public static float CalculatePopupTargetHeight(int rowsOnPage, bool includePagination)
        {
            int clampedRows = Mathf.Max(1, rowsOnPage);
            float chromeHeight = CalculatePopupChromeHeight(includePagination);
            float optionListHeight = clampedRows * GetOptionRowHeight();
            float unclampedHeight = chromeHeight + optionListHeight;
            return unclampedHeight;
        }

        /// <summary>
        /// Calculates the chrome (non-content) height of popup windows.
        /// </summary>
        /// <param name="includePagination">Whether pagination controls are visible.</param>
        /// <returns>The chrome height in pixels.</returns>
        public static float CalculatePopupChromeHeight(bool includePagination)
        {
            EnsureStylesInitialized();
            float searchHeight = EditorGUIUtility.singleLineHeight;
            float paginationHeight = includePagination
                ? s_paginationButtonLeft.fixedHeight
                : EditorGUIUtility.standardVerticalSpacing;
            float footerHeight = EditorGUIUtility.standardVerticalSpacing + OptionBottomPadding;
            return searchHeight + paginationHeight + footerHeight;
        }

        /// <summary>
        /// Calculates the height for empty search results state.
        /// </summary>
        /// <param name="measuredHelpBoxHeight">Optional measured help box height, or -1f to calculate.</param>
        /// <returns>The empty search height in pixels.</returns>
        public static float CalculateEmptySearchHeight(float measuredHelpBoxHeight = -1f)
        {
            GUIStyle helpStyle = EditorStyles.helpBox;
            int helpMargin = helpStyle.margin?.horizontal ?? 0;
            float availableWidth = PopupWidth - EmptySearchHorizontalPadding - helpMargin;
            availableWidth = Mathf.Max(32f, availableWidth);
            float helpBoxHeight;
            if (0f < measuredHelpBoxHeight)
            {
                helpBoxHeight = measuredHelpBoxHeight;
            }
            else
            {
                float calculated = helpStyle.CalcHeight(EmptyResultsContent, availableWidth);
                float marginVertical = helpStyle.margin?.vertical ?? 0;
                helpBoxHeight = calculated + marginVertical;
            }

            float searchRow =
                EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            float topSpacer = EditorGUIUtility.standardVerticalSpacing;
            float bottomSpacer = EditorGUIUtility.standardVerticalSpacing;
            float footer =
                EditorGUIUtility.standardVerticalSpacing
                + OptionBottomPadding
                + EmptySearchExtraPadding;

            float result = searchRow + topSpacer + helpBoxHeight + bottomSpacer + footer;
            return result;
        }

        /// <summary>
        /// Gets the height of an option row including margins.
        /// </summary>
        /// <returns>The row height in pixels.</returns>
        public static float GetOptionRowHeight()
        {
            if (0f < s_cachedOptionRowHeight)
            {
                return s_cachedOptionRowHeight;
            }

            float controlHeight = GetOptionControlHeight();
            EnsureStylesInitialized();
            RectOffset margin = s_optionButton.margin;
            float adjustedMargin;
            if (margin != null)
            {
                adjustedMargin = Mathf.Max(
                    0f,
                    margin.vertical - EditorGUIUtility.standardVerticalSpacing
                );
            }
            else
            {
                adjustedMargin = EditorGUIUtility.standardVerticalSpacing;
            }

            s_cachedOptionRowHeight = controlHeight + adjustedMargin;
            return s_cachedOptionRowHeight;
        }

        /// <summary>
        /// Gets the height of an option button control.
        /// </summary>
        /// <returns>The control height in pixels.</returns>
        public static float GetOptionControlHeight()
        {
            if (0f < s_cachedOptionControlHeight)
            {
                return s_cachedOptionControlHeight;
            }

            EnsureStylesInitialized();
            float width = PopupWidth - 32f;
            float measured = s_optionButton.CalcHeight(GUIContent.none, width);
            if (measured <= 0f || float.IsNaN(measured))
            {
                measured = EditorGUIUtility.singleLineHeight + OptionRowExtraHeight;
            }

            s_cachedOptionControlHeight = measured;
            return measured;
        }

        /// <summary>
        /// Computes a hash code for an array of integer options.
        /// Used for caching display option arrays.
        /// </summary>
        /// <param name="options">The array of integer options.</param>
        /// <returns>A hash code for the options array.</returns>
        public static int ComputeOptionsHash(int[] options)
        {
            return Objects.SpanHashCode<int>(options);
        }

        /// <summary>
        /// Checks if a type is a numeric type (integer or floating-point).
        /// </summary>
        /// <param name="type">The type to check.</param>
        /// <returns>True if the type is numeric, false otherwise.</returns>
        public static bool IsNumericType(Type type)
        {
            return type == typeof(int)
                || type == typeof(long)
                || type == typeof(short)
                || type == typeof(byte)
                || type == typeof(sbyte)
                || type == typeof(uint)
                || type == typeof(ulong)
                || type == typeof(ushort)
                || type == typeof(float)
                || type == typeof(double)
                || type == typeof(decimal);
        }

        /// <summary>
        /// Checks if a type is an integer type.
        /// </summary>
        /// <param name="type">The type to check.</param>
        /// <returns>True if the type is an integer type, false otherwise.</returns>
        public static bool IsIntegerType(Type type)
        {
            return type == typeof(int)
                || type == typeof(long)
                || type == typeof(short)
                || type == typeof(byte)
                || type == typeof(sbyte)
                || type == typeof(uint)
                || type == typeof(ulong)
                || type == typeof(ushort);
        }

        /// <summary>
        /// Compares two values with support for numeric type coercion and enum handling.
        /// </summary>
        /// <param name="a">The first value.</param>
        /// <param name="b">The second value.</param>
        /// <returns>True if the values are considered equal, false otherwise.</returns>
        public static bool ValuesMatch(object a, object b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (a == null || b == null)
            {
                return false;
            }

            if (a.Equals(b))
            {
                return true;
            }

            if (a is UnityEngine.Object unityA && b is UnityEngine.Object unityB)
            {
                return unityA == unityB;
            }

            Type typeA = a.GetType();
            Type typeB = b.GetType();

            if (IsNumericType(typeA) && IsNumericType(typeB))
            {
                try
                {
                    double numA = Convert.ToDouble(a);
                    double numB = Convert.ToDouble(b);
                    return Math.Abs(numA - numB) < double.Epsilon;
                }
                catch
                {
                    return false;
                }
            }

            if (typeA.IsEnum && IsIntegerType(typeB))
            {
                try
                {
                    long enumValue = Convert.ToInt64(a);
                    long intValue = Convert.ToInt64(b);
                    return enumValue == intValue;
                }
                catch
                {
                    return false;
                }
            }

            if (typeB.IsEnum && IsIntegerType(typeA))
            {
                try
                {
                    long enumValue = Convert.ToInt64(b);
                    long intValue = Convert.ToInt64(a);
                    return enumValue == intValue;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Clears all cached data. Useful for testing or when options change significantly.
        /// Note: IntToString and PaginationLabel caches are managed centrally by EditorCacheHelper.
        /// </summary>
        public static void ClearAllCaches()
        {
            FormattedOptionCache.Clear();
            EnumDisplayNameCache.Clear();
            s_cachedOptionControlHeight = -1f;
            s_cachedOptionRowHeight = -1f;
        }

        /// <summary>
        /// Clears only the formatted option cache. Useful when object names may have changed.
        /// </summary>
        public static void ClearFormattedOptionCache()
        {
            FormattedOptionCache.Clear();
        }

        private static void EnsureStylesInitialized()
        {
            if (s_optionButton != null)
            {
                return;
            }

            s_optionButton = new GUIStyle("Button")
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(6, 6, 1, 1),
            };
            s_selectedOptionButton = new GUIStyle(s_optionButton) { fontStyle = FontStyle.Bold };
            float paginationHeight = PaginationButtonHeight;
            s_paginationButtonLeft = new GUIStyle(EditorStyles.miniButtonLeft)
            {
                fixedHeight = paginationHeight,
                padding = new RectOffset(6, 6, 0, 0),
            };
            s_paginationButtonRight = new GUIStyle(EditorStyles.miniButtonRight)
            {
                fixedHeight = paginationHeight,
                padding = new RectOffset(6, 6, 0, 0),
            };
            s_paginationLabel = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(0, 0, 0, 0),
            };
        }

        /// <summary>
        /// Returns a fallback display label for an option at the given index.
        /// Used when the option's raw display label is null or empty.
        /// </summary>
        /// <param name="optionIndex">The index of the option.</param>
        /// <returns>A fallback label in the format "(Option N)".</returns>
        public static string GetFallbackOptionLabel(int optionIndex)
        {
            if (!FallbackOptionLabelCache.TryGetValue(optionIndex, out string cached))
            {
                cached = $"(Option {optionIndex})";
                FallbackOptionLabelCache[optionIndex] = cached;
            }
            return cached;
        }

        /// <summary>
        /// Test hooks for unit testing internal functionality.
        /// </summary>
        internal static class TestHooks
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
    }
#endif
}
