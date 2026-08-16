// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils
{
#if UNITY_EDITOR

    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Runtime.CompilerServices;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils.WButton;

    /// <summary>
    /// Provides shared constants, types, and helper methods for enum toggle button drawers.
    /// </summary>
    /// <remarks>
    /// This utility class consolidates common code used by both the standard PropertyDrawer
    /// and the Odin Inspector drawer implementations of WEnumToggleButtons. By centralizing
    /// these elements, we ensure consistent behavior and eliminate code duplication.
    /// </remarks>
    public static class EnumToggleButtonsShared
    {
        /// <summary>
        /// Default padding around buttons.
        /// </summary>
        public const float ButtonPadding = 6f;

        /// <summary>
        /// Padding around selected items.
        /// </summary>
        public const float SelectionPadding = 8f;

        /// <summary>
        /// Default toolbar height.
        /// </summary>
        public const float ToolbarHeight = 68f;

        /// <summary>
        /// Height of selected item display.
        /// </summary>
        public const float SelectedItemHeight = 60f;

        /// <summary>
        /// Height of labels.
        /// </summary>
        public const float LabelHeight = 22f;

        /// <summary>
        /// Minimum width for buttons.
        /// </summary>
        public const float MinButtonWidth = 80f;

        /// <summary>
        /// Vertical spacing between button rows.
        /// </summary>
        public const float ButtonVerticalSpacing = 2f;

        /// <summary>
        /// Horizontal spacing between buttons.
        /// </summary>
        public const float ButtonHorizontalSpacing = 5f;

        /// <summary>
        /// Spacing between toolbar elements.
        /// </summary>
        public const float ToolbarSpacing = 6f;

        /// <summary>
        /// Gap between toolbar buttons.
        /// </summary>
        public const float ToolbarButtonGap = 8f;

        /// <summary>
        /// Minimum width for toolbar buttons.
        /// </summary>
        public const float ToolbarButtonMinWidth = 60f;

        /// <summary>
        /// Width of pagination navigation buttons.
        /// </summary>
        public const float PaginationButtonWidth = 22f;

        /// <summary>
        /// Minimum width for pagination label.
        /// </summary>
        public const float PaginationLabelMinWidth = 80f;

        /// <summary>
        /// Spacing above and below summary text.
        /// </summary>
        public const float SummarySpacing = 2f;

        /// <summary>
        /// Ratio used to split available width evenly between two aligned buttons (e.g., Select All / None).
        /// </summary>
        public const float EqualSplitRatio = 0.5f;

        /// <summary>
        /// Maximum ratio of the pagination area width allocated to each navigation button.
        /// Ensures buttons don't become excessively wide in large layouts.
        /// </summary>
        public const float MaxPaginationButtonWidthRatio = 0.2f;

        /// <summary>
        /// Ratio used to center elements when distributing overflow correction.
        /// Applies half the overflow adjustment to shift elements toward center.
        /// </summary>
        public const float OverflowCenteringRatio = 0.5f;

        /// <summary>
        /// Content for navigating to previous page.
        /// </summary>
        public static readonly GUIContent PrevPageContent = new("◀", "Previous Page");

        /// <summary>
        /// Content for navigating to next page.
        /// </summary>
        public static readonly GUIContent NextPageContent = new("▶", "Next Page");

        /// <summary>
        /// Content for the "None" selection button.
        /// </summary>
        public static readonly GUIContent NoneContent = new("None");

        /// <summary>
        /// Content for the "All" selection button.
        /// </summary>
        public static readonly GUIContent AllContent = new("All");

        /// <summary>
        /// Content for expand indicator.
        /// </summary>
        public static readonly GUIContent ExpandContent = new("▼");

        /// <summary>
        /// Content for collapse indicator.
        /// </summary>
        public static readonly GUIContent CollapseContent = new("▲");

        /// <summary>
        /// Content for search indicator.
        /// </summary>
        public static readonly GUIContent SearchContent = new("🔍", "Search");

        // Lazy initialization to avoid calling EditorGUIUtility during static class loading,
        // which can hang Unity during "Open Project: Open Scene" if the class is accessed
        // before EditorGUIUtility is fully initialized.
        private static GUIContent _firstPageContent;
        private static GUIContent _lastPageContent;

        /// <summary>
        /// Content for navigating to first page.
        /// </summary>
        public static GUIContent FirstPageContent =>
            _firstPageContent ??= EditorGUIUtility.TrTextContent("<<", "First Page");

        /// <summary>
        /// Content for navigating to last page.
        /// </summary>
        public static GUIContent LastPageContent =>
            _lastPageContent ??= EditorGUIUtility.TrTextContent(">>", "Last Page");

        private static readonly EditorCacheHelper.ColorComparer ColorEquality = new();

        private static readonly Dictionary<ButtonStyleCacheKey, GUIStyle> ButtonStyleCache = new(
            new ButtonStyleCacheKeyComparer()
        );

        private static GUIStyle _summaryStyle;

        private const string SummaryStyleKey = "EnumToggleButtons/SummaryStyle";

        /// <summary>
        /// Gets the style used for displaying selection summary text.
        /// </summary>
        public static GUIStyle SummaryStyle
        {
            get
            {
                if (_summaryStyle != null)
                {
                    return _summaryStyle;
                }

                _summaryStyle = EditorCacheHelper.GetOrCreateStyle(
                    SummaryStyleKey,
                    CreateSummaryStyle
                );

                return _summaryStyle;
            }
        }

        private static GUIStyle CreateSummaryStyle()
        {
            return new GUIStyle(EditorStyles.wordWrappedMiniLabel) { fontStyle = FontStyle.Italic };
        }

        /// <summary>
        /// Determines which segment style a button should use based on its position.
        /// </summary>
        /// <param name="index">The zero-based index of the button in the visible set.</param>
        /// <param name="total">The total number of visible buttons.</param>
        /// <param name="columns">The number of columns in the layout.</param>
        /// <returns>The appropriate <see cref="ButtonSegment"/> for the button position.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ButtonSegment ResolveButtonSegment(int index, int total, int columns)
        {
            if (columns <= 1)
            {
                return ButtonSegment.Single;
            }

            int columnIndex = index % columns;

            bool isFirst = columnIndex == 0;

            bool isLast = columnIndex == columns - 1 || index == total - 1;

            if (isFirst && isLast)
            {
                return ButtonSegment.Single;
            }

            if (isFirst)
            {
                return ButtonSegment.Left;
            }

            if (isLast)
            {
                return ButtonSegment.Right;
            }

            return ButtonSegment.Middle;
        }

        /// <summary>
        /// Calculates the layout metrics for displaying toggle buttons.
        /// </summary>
        /// <param name="requestedPerRow">The requested number of buttons per row, or 0 for auto.</param>
        /// <param name="optionCount">The total number of options to display.</param>
        /// <param name="availableWidth">The available width for the button layout.</param>
        /// <param name="lineHeight">The height of a single button row.</param>
        /// <param name="spacing">The spacing between buttons.</param>
        /// <param name="minWidth">The minimum width for a single button.</param>
        /// <returns>A <see cref="LayoutMetrics"/> describing the calculated layout.</returns>
        public static LayoutMetrics CalculateLayout(
            int requestedPerRow,
            int optionCount,
            float availableWidth,
            float lineHeight,
            float spacing,
            float minWidth
        )
        {
            if (optionCount <= 0)
            {
                return new LayoutMetrics(1, 0, minWidth, lineHeight, spacing, 0f);
            }

            if (optionCount == 1)
            {
                float singleWidth = Mathf.Max(minWidth, availableWidth);

                return new LayoutMetrics(1, 1, singleWidth, lineHeight, spacing, lineHeight);
            }

            int columns =
                requestedPerRow > 0
                    ? requestedPerRow
                    : DetermineAutoColumns(availableWidth, spacing, minWidth);

            columns = Mathf.Clamp(columns, 1, optionCount);

            int rows = Mathf.CeilToInt(optionCount / (float)columns);

            float workingWidth = availableWidth;

            if (workingWidth <= 0f)
            {
                workingWidth = columns * minWidth + (columns - 1) * spacing;
            }

            float buttonWidth = (workingWidth - (columns - 1) * spacing) / columns;

            if (buttonWidth < minWidth)
            {
                buttonWidth = minWidth;
            }

            float totalHeight = rows * lineHeight + Mathf.Max(0, rows - 1) * spacing;

            return new LayoutMetrics(columns, rows, buttonWidth, lineHeight, spacing, totalHeight);
        }

        /// <summary>
        /// Determines the optimal number of columns based on available width.
        /// </summary>
        /// <param name="availableWidth">The available width for the layout.</param>
        /// <param name="spacing">The spacing between buttons.</param>
        /// <param name="minWidth">The minimum width for each button.</param>
        /// <returns>The optimal number of columns, at least 1.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int DetermineAutoColumns(float availableWidth, float spacing, float minWidth)
        {
            if (availableWidth <= 0f)
            {
                return 1;
            }

            float effectiveWidth = minWidth + spacing;

            int columns = Mathf.FloorToInt((availableWidth + spacing) / effectiveWidth);

            if (columns < 1)
            {
                columns = 1;
            }

            return columns;
        }

        /// <summary>
        /// Checks if a value is a power of two.
        /// </summary>
        /// <param name="value">The value to check.</param>
        /// <returns>True if the value is a power of two and not zero; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsPowerOfTwo(ulong value)
        {
            return value != 0UL && (value & (value - 1UL)) == 0UL;
        }

        /// <summary>
        /// Gets the bit mask covering the width of an enum's underlying type.
        /// </summary>
        /// <param name="enumType">The enum type to measure.</param>
        /// <returns>The mask, or <see cref="ulong.MaxValue"/> for a non-enum or 64-bit type.</returns>
        /// <remarks>
        /// <see cref="ConvertToUInt64"/> sign-extends, so a legitimate top-bit flag such as
        /// <c>sbyte -128</c> becomes <c>0xFFFFFFFFFFFFFF80</c>. That is the right value for mask
        /// arithmetic -- it matches what the serialized property reads back -- but it is not a
        /// power of two, so a power-of-two check must run against the truncated width or the flag
        /// is discarded as composite.
        /// </remarks>
        public static ulong GetUnderlyingTypeMask(Type enumType)
        {
            if (enumType == null || !enumType.IsEnum)
            {
                return ulong.MaxValue;
            }

            switch (Type.GetTypeCode(Enum.GetUnderlyingType(enumType)))
            {
                case TypeCode.SByte:
                case TypeCode.Byte:
                    return byte.MaxValue;
                case TypeCode.Int16:
                case TypeCode.UInt16:
                    return ushort.MaxValue;
                case TypeCode.Int32:
                case TypeCode.UInt32:
                    return uint.MaxValue;
                default:
                    return ulong.MaxValue;
            }
        }

        /// <summary>
        /// Converts an object value to a UInt64 representation.
        /// </summary>
        /// <param name="value">The value to convert (typically an enum value).</param>
        /// <returns>The UInt64 representation, or 0 if conversion fails.</returns>
        /// <remarks>
        /// Enum values route through <see cref="EnumExtensions.TryConvertToUInt64(Enum, out ulong)"/>, which
        /// sign-extends signed underlying types. <see cref="Convert.ToUInt64(object, IFormatProvider)"/>
        /// throws <see cref="OverflowException"/> on every negative member, and the catch below used
        /// to turn that into mask 0 -- rendering and round-tripping such a member as "None".
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ConvertToUInt64(object value)
        {
            if (value == null)
            {
                return 0UL;
            }

            if (value is Enum enumValue)
            {
                return enumValue.TryConvertToUInt64(out ulong converted) ? converted : 0UL;
            }

            try
            {
                return Convert.ToUInt64(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0UL;
            }
        }

        /// <summary>
        /// Gets a cached button style for the specified segment and state.
        /// </summary>
        /// <param name="segment">The button segment (Single, Left, Middle, Right).</param>
        /// <param name="isActive">Whether the button is in an active (selected) state.</param>
        /// <param name="selectedBg">The background color when selected.</param>
        /// <param name="selectedText">The text color when selected.</param>
        /// <param name="inactiveBg">The background color when not selected.</param>
        /// <param name="inactiveText">The text color when not selected.</param>
        /// <returns>A cached <see cref="GUIStyle"/> configured for the button.</returns>
        public static GUIStyle GetButtonStyle(
            ButtonSegment segment,
            bool isActive,
            Color selectedBg,
            Color selectedText,
            Color inactiveBg,
            Color inactiveText
        )
        {
            ButtonStyleCacheKey key = new(
                segment,
                isActive,
                selectedBg,
                selectedText,
                inactiveBg,
                inactiveText
            );

            if (ButtonStyleCache.TryGetValue(key, out GUIStyle cached))
            {
                return cached;
            }

            GUIStyle basis = segment switch
            {
                ButtonSegment.Left => EditorStyles.miniButtonLeft,

                ButtonSegment.Middle => EditorStyles.miniButtonMid,

                ButtonSegment.Right => EditorStyles.miniButtonRight,

                _ => EditorStyles.miniButton,
            };

            GUIStyle style = new(basis)
            {
                name =
                    "EnumToggleButtons/"
                    + segment.ToString()
                    + "/"
                    + (isActive ? "Active" : "Inactive"),
            };

            Color baseBackground = isActive ? selectedBg : inactiveBg;

            Color hoverBackground = WButtonColorUtility.GetHoverColor(baseBackground);

            Color activeBackground = WButtonColorUtility.GetActiveColor(baseBackground);

            Color textColor = isActive ? selectedText : inactiveText;

            ConfigureButtonStyle(
                style,
                baseBackground,
                hoverBackground,
                activeBackground,
                textColor
            );

            ButtonStyleCache[key] = style;

            return style;
        }

        /// <summary>
        /// Gets a cached button style using a palette entry.
        /// </summary>
        /// <param name="segment">The button segment (Single, Left, Middle, Right).</param>
        /// <param name="isActive">Whether the button is in an active (selected) state.</param>
        /// <param name="palette">The color palette entry to use.</param>
        /// <returns>A cached <see cref="GUIStyle"/> configured for the button.</returns>
        public static GUIStyle GetButtonStyle(
            ButtonSegment segment,
            bool isActive,
            UnityHelpersSettings.WEnumToggleButtonsPaletteEntry palette
        )
        {
            return GetButtonStyle(
                segment,
                isActive,
                palette.SelectedBackgroundColor,
                palette.SelectedTextColor,
                palette.InactiveBackgroundColor,
                palette.InactiveTextColor
            );
        }

        /// <summary>
        /// Configures a GUIStyle with the specified colors for all states.
        /// </summary>
        /// <param name="style">The style to configure.</param>
        /// <param name="normalBg">The background color for normal state.</param>
        /// <param name="hoverBg">The background color for hover state.</param>
        /// <param name="activeBg">The background color for active/pressed state.</param>
        /// <param name="textColor">The text color for all states.</param>
        public static void ConfigureButtonStyle(
            GUIStyle style,
            Color normalBg,
            Color hoverBg,
            Color activeBg,
            Color textColor
        )
        {
            if (style == null)
            {
                return;
            }

            Texture2D normalTexture = EditorCacheHelper.GetOrCreateTexture(normalBg);

            Texture2D hoverTexture = EditorCacheHelper.GetOrCreateTexture(hoverBg);

            Texture2D activeTexture = EditorCacheHelper.GetOrCreateTexture(activeBg);

            style.normal.background = normalTexture;

            style.normal.textColor = textColor;

            style.focused.background = normalTexture;

            style.focused.textColor = textColor;

            style.onNormal.background = normalTexture;

            style.onNormal.textColor = textColor;

            style.onFocused.background = normalTexture;

            style.onFocused.textColor = textColor;

            style.hover.background = hoverTexture;

            style.hover.textColor = textColor;

            style.onHover.background = hoverTexture;

            style.onHover.textColor = textColor;

            style.active.background = activeTexture;

            style.active.textColor = textColor;

            style.onActive.background = activeTexture;

            style.onActive.textColor = textColor;
        }

        /// <summary>
        /// Clears all cached button styles. Call when theme or colors change.
        /// </summary>
        public static void ClearStyleCache()
        {
            ButtonStyleCache.Clear();

            _summaryStyle = null;
        }

        /// <summary>
        /// Defines the visual segment of a button in a horizontal toolbar layout.
        /// </summary>
        public enum ButtonSegment
        {
            /// <summary>A standalone button with no adjacent buttons.</summary>
            Single = 0,

            /// <summary>The leftmost button in a group.</summary>
            Left = 1,

            /// <summary>A middle button in a group.</summary>
            Middle = 2,

            /// <summary>The rightmost button in a group.</summary>
            Right = 3,
        }

        /// <summary>
        /// Cache key for button styles, incorporating all visual parameters.
        /// </summary>
        public readonly struct ButtonStyleCacheKey : IEquatable<ButtonStyleCacheKey>
        {
            /// <summary>
            /// Gets the button segment type.
            /// </summary>
            public ButtonSegment Segment { get; }

            /// <summary>
            /// Gets whether the button is in an active state.
            /// </summary>
            public bool IsActive { get; }

            /// <summary>
            /// Gets the selected background color.
            /// </summary>
            public Color SelectedBackground { get; }

            /// <summary>
            /// Gets the selected text color.
            /// </summary>
            public Color SelectedText { get; }

            /// <summary>
            /// Gets the inactive background color.
            /// </summary>
            public Color InactiveBackground { get; }

            /// <summary>
            /// Gets the inactive text color.
            /// </summary>
            public Color InactiveText { get; }

            /// <summary>
            /// Creates a new button style cache key.
            /// </summary>
            /// <param name="segment">The button segment type.</param>
            /// <param name="isActive">Whether the button is active.</param>
            /// <param name="selectedBackground">The selected background color.</param>
            /// <param name="selectedText">The selected text color.</param>
            /// <param name="inactiveBackground">The inactive background color.</param>
            /// <param name="inactiveText">The inactive text color.</param>
            public ButtonStyleCacheKey(
                ButtonSegment segment,
                bool isActive,
                Color selectedBackground,
                Color selectedText,
                Color inactiveBackground,
                Color inactiveText
            )
            {
                Segment = segment;

                IsActive = isActive;

                SelectedBackground = selectedBackground;

                SelectedText = selectedText;

                InactiveBackground = inactiveBackground;

                InactiveText = inactiveText;
            }

            /// <summary>
            /// Creates a new button style cache key from a palette entry.
            /// </summary>
            /// <param name="segment">The button segment type.</param>
            /// <param name="isActive">Whether the button is active.</param>
            /// <param name="palette">The color palette entry.</param>
            public ButtonStyleCacheKey(
                ButtonSegment segment,
                bool isActive,
                UnityHelpersSettings.WEnumToggleButtonsPaletteEntry palette
            )
            {
                Segment = segment;

                IsActive = isActive;

                SelectedBackground = palette.SelectedBackgroundColor;

                SelectedText = palette.SelectedTextColor;

                InactiveBackground = palette.InactiveBackgroundColor;

                InactiveText = palette.InactiveTextColor;
            }

            /// <inheritdoc />
            public bool Equals(ButtonStyleCacheKey other)
            {
                return Segment == other.Segment
                    && IsActive == other.IsActive
                    && ColorEquality.Equals(SelectedBackground, other.SelectedBackground)
                    && ColorEquality.Equals(SelectedText, other.SelectedText)
                    && ColorEquality.Equals(InactiveBackground, other.InactiveBackground)
                    && ColorEquality.Equals(InactiveText, other.InactiveText);
            }

            /// <inheritdoc />
            public override bool Equals(object obj)
            {
                return obj is ButtonStyleCacheKey other && Equals(other);
            }

            /// <inheritdoc />
            public override int GetHashCode()
            {
                return Objects.HashCode(
                    Segment,
                    IsActive,
                    EditorCacheHelper.GetColorHashCode(SelectedBackground),
                    EditorCacheHelper.GetColorHashCode(SelectedText),
                    EditorCacheHelper.GetColorHashCode(InactiveBackground),
                    EditorCacheHelper.GetColorHashCode(InactiveText)
                );
            }
        }

        /// <summary>
        /// Comparer for <see cref="ButtonStyleCacheKey"/> used in dictionary lookups.
        /// </summary>
        public sealed class ButtonStyleCacheKeyComparer : IEqualityComparer<ButtonStyleCacheKey>
        {
            /// <inheritdoc />
            public bool Equals(ButtonStyleCacheKey x, ButtonStyleCacheKey y)
            {
                return x.Equals(y);
            }

            /// <inheritdoc />
            public int GetHashCode(ButtonStyleCacheKey obj)
            {
                return obj.GetHashCode();
            }
        }

        /// <summary>
        /// Represents a single toggle option in an enum toggle button drawer.
        /// </summary>
        public readonly struct ToggleOption
        {
            /// <summary>
            /// Gets the display label for the option.
            /// </summary>
            public string Label { get; }

            /// <summary>
            /// Gets the original enum value.
            /// </summary>
            public object Value { get; }

            /// <summary>
            /// Gets the numeric flag value as UInt64.
            /// </summary>
            public ulong FlagValue { get; }

            /// <summary>
            /// Gets whether this represents a zero-valued flag (typically "None").
            /// </summary>
            public bool IsZeroFlag { get; }

            /// <summary>
            /// Creates a new toggle option.
            /// </summary>
            /// <param name="label">The display label.</param>
            /// <param name="value">The enum value.</param>
            /// <param name="flagValue">The numeric flag value.</param>
            /// <param name="isZeroFlag">Whether this is a zero flag.</param>
            public ToggleOption(string label, object value, ulong flagValue, bool isZeroFlag)
            {
                Label = string.IsNullOrEmpty(label) ? "(Unnamed)" : label;

                Value = value;

                FlagValue = flagValue;

                IsZeroFlag = isZeroFlag;
            }
        }

        /// <summary>
        /// Represents a summary of selected items not visible on the current page.
        /// </summary>
        public readonly struct SelectionSummary
        {
            /// <summary>
            /// Gets a summary indicating no out-of-view selections.
            /// </summary>
            public static SelectionSummary None { get; } = new(false, GUIContent.none);

            /// <summary>
            /// Gets whether there is summary content to display.
            /// </summary>
            public bool HasSummary { get; }

            /// <summary>
            /// Gets the GUI content to display.
            /// </summary>
            public GUIContent Content { get; }

            /// <summary>
            /// Creates a new selection summary.
            /// </summary>
            /// <param name="hasSummary">Whether there is summary content.</param>
            /// <param name="content">The content to display.</param>
            public SelectionSummary(bool hasSummary, GUIContent content)
            {
                HasSummary = hasSummary;

                Content = content ?? GUIContent.none;
            }
        }

        /// <summary>
        /// Describes the calculated layout metrics for displaying toggle buttons.
        /// </summary>
        public readonly struct LayoutMetrics
        {
            /// <summary>
            /// Gets the number of columns in the layout.
            /// </summary>
            public int Columns { get; }

            /// <summary>
            /// Gets the number of rows in the layout.
            /// </summary>
            public int Rows { get; }

            /// <summary>
            /// Gets the width of each button.
            /// </summary>
            public float ButtonWidth { get; }

            /// <summary>
            /// Gets the height of each button.
            /// </summary>
            public float ButtonHeight { get; }

            /// <summary>
            /// Gets the spacing between buttons.
            /// </summary>
            public float Spacing { get; }

            /// <summary>
            /// Gets the total height of the button layout.
            /// </summary>
            public float TotalHeight { get; }

            /// <summary>
            /// Creates new layout metrics.
            /// </summary>
            /// <param name="columns">The number of columns.</param>
            /// <param name="rows">The number of rows.</param>
            /// <param name="buttonWidth">The button width.</param>
            /// <param name="buttonHeight">The button height.</param>
            /// <param name="spacing">The spacing between buttons.</param>
            /// <param name="totalHeight">The total layout height.</param>
            public LayoutMetrics(
                int columns,
                int rows,
                float buttonWidth,
                float buttonHeight,
                float spacing,
                float totalHeight
            )
            {
                Columns = columns;

                Rows = rows;

                ButtonWidth = buttonWidth;

                ButtonHeight = buttonHeight;

                Spacing = spacing;

                TotalHeight = totalHeight;
            }

            /// <summary>
            /// Gets the rectangle for a button at the specified index.
            /// </summary>
            /// <param name="bounds">The bounding rectangle for the entire button area.</param>
            /// <param name="index">The zero-based index of the button.</param>
            /// <returns>The rectangle where the button should be drawn.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Rect GetItemRect(Rect bounds, int index)
            {
                int row = index / Columns;

                int column = index % Columns;

                float x = bounds.x + column * (ButtonWidth + Spacing);

                float y = bounds.y + row * (ButtonHeight + Spacing);

                return new Rect(x, y, ButtonWidth, ButtonHeight);
            }
        }

        /// <summary>
        /// Tracks pagination state for a property.
        /// </summary>
        public sealed class PaginationState
        {
            private int _pageIndex;

            /// <summary>
            /// Gets or sets the number of items per page.
            /// </summary>
            public int PageSize { get; set; }

            /// <summary>
            /// Gets or sets the total number of items.
            /// </summary>
            public int TotalItems { get; set; }

            /// <summary>
            /// Gets or sets the current page index (0-based).
            /// </summary>
            public int PageIndex
            {
                get => _pageIndex;
                set => _pageIndex = value;
            }

            /// <summary>
            /// Gets the total number of pages.
            /// </summary>
            public int TotalPages
            {
                get
                {
                    if (PageSize <= 0)
                    {
                        return 1;
                    }

                    return Mathf.Max(1, Mathf.CeilToInt(TotalItems / (float)PageSize));
                }
            }

            /// <summary>
            /// Gets the starting index of items on the current page.
            /// </summary>
            public int StartIndex
            {
                get
                {
                    if (TotalItems <= 0 || PageSize <= 0)
                    {
                        return 0;
                    }

                    int clampedIndex = Mathf.Clamp(PageIndex, 0, TotalPages - 1);

                    return clampedIndex * PageSize;
                }
            }

            /// <summary>
            /// Gets the number of items visible on the current page.
            /// </summary>
            public int VisibleCount
            {
                get
                {
                    if (TotalItems <= 0 || PageSize <= 0)
                    {
                        return 0;
                    }

                    int clampedIndex = Mathf.Clamp(PageIndex, 0, TotalPages - 1);

                    int start = clampedIndex * PageSize;

                    return Mathf.Clamp(TotalItems - start, 0, PageSize);
                }
            }
        }
    }

#endif
}
