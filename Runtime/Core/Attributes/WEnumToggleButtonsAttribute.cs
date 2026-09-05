// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using UnityEngine;

    /// <summary>
    /// Draws enum and dropdown-backed fields as a toolbar of toggle buttons.
    /// Designed to mirror Odin Inspector's <c>EnumToggleButtons</c> experience while remaining editor-only and dependency free.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When applied to a field backed by a <c>[Flags]</c> enum, each discrete flag is rendered as an individual toggle button.
    /// This makes it significantly easier to reason about composite bitmasks in the inspector compared to Unity's built-in mask field.
    /// </para>
    /// <para>
    /// Throughout the documentation this workflow is often referred to as <c>WEnumToggleFlags</c>; the attribute name remains
    /// <see cref="WEnumToggleButtonsAttribute"/> but no additional setup is required—simply annotate the flagged enum field.
    /// </para>
    /// <para>
    /// The attribute can also be combined with <see cref="IntDropDownAttribute"/>, <see cref="StringInListAttribute"/>, or
    /// <see cref="WValueDropDownAttribute"/> to render those curated lists as toggle buttons instead of a popup.
    /// In this mode only a single option is active at a time, but the visual presentation becomes much clearer for small choice sets.
    /// </para>
    /// <para>
    /// Because everything is driven through serialized fields and reflection, the attribute remains editor-safe and transparent to build players.
    /// No runtime components are introduced and the backing value on the component or asset stays unchanged.
    /// </para>
    /// <para>
    /// Large option sets automatically paginate based on the Unity Helpers project settings.
    /// Set <see cref="EnablePagination"/> to <see langword="false"/> or supply a custom <see cref="PageSize"/> to override that behaviour.
    /// </para>
    /// </remarks>
    /// <example>
    /// Flag-based enums:
    /// <code>
    /// [System.Flags]
    /// public enum MovementCapabilities
    /// {
    ///     None = 0,
    ///     Walk = 1 &lt;&lt; 0,
    ///     Jump = 1 &lt;&lt; 1,
    ///     Swim = 1 &lt;&lt; 2,
    ///     Fly = 1 &lt;&lt; 3,
    /// }
    ///
    /// [WEnumToggleButtons(ButtonsPerRow = 3)]
    /// public MovementCapabilities unlockedAbilities;
    /// </code>
    /// Dropdown-backed fields:
    /// <code>
    /// [WEnumToggleButtons(ShowSelectNone = false)]
    /// [StringInList("Low", "Medium", "High")]
    /// public string difficulty;
    ///
    /// [WEnumToggleButtons(ButtonsPerRow = 4)]
    /// [IntDropDown(15, 30, 45, 60)]
    /// public int targetFrameRate;
    ///
    /// [WEnumToggleButtons]
    /// [WValueDropDown(typeof(LocalizationCatalogue), nameof(LocalizationCatalogue.GetKnownKeys), typeof(string))]
    /// public string localizationKey;
    /// </code>
    /// </example>
    // Explicit targets prevent Unity-version drift and include serialized properties.
    [AttributeUsage(
        AttributeTargets.Field | AttributeTargets.Property,
        AllowMultiple = false,
        Inherited = true
    )]
    public sealed class WEnumToggleButtonsAttribute : PropertyAttribute
    {
        private string _colorKey;

        /// <summary>
        /// Initializes the attribute with automatic row sizing.
        /// </summary>
        public WEnumToggleButtonsAttribute()
            : this(
                buttonsPerRow: 0,
                showSelectAll: true,
                showSelectNone: true,
                enablePagination: true,
                pageSize: 0,
                colorKey: null
            ) { }

        /// <summary>
        /// Initializes the attribute with a fixed number of buttons per row.
        /// </summary>
        /// <param name="buttonsPerRow">
        /// Number of buttons to render per row. Values below one fall back to automatic sizing.
        /// Use zero to keep the automatic layout behaviour while still enabling the optional toolbar controls.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="buttonsPerRow"/> is negative.</exception>
        public WEnumToggleButtonsAttribute(int buttonsPerRow)
            : this(
                buttonsPerRow: buttonsPerRow,
                showSelectAll: true,
                showSelectNone: true,
                enablePagination: true,
                pageSize: 0,
                colorKey: null
            ) { }

        /// <summary>
        /// Initializes the attribute with full control over all configuration options.
        /// </summary>
        /// <param name="buttonsPerRow">
        /// Number of buttons to render per row. Values below one fall back to automatic sizing.
        /// Use zero to keep the automatic layout behaviour while still enabling the optional toolbar controls.
        /// </param>
        /// <param name="showSelectAll">
        /// Whether a quick action button for selecting every flag should be displayed.
        /// Only meaningful for <c>[Flags]</c> enums.
        /// </param>
        /// <param name="showSelectNone">
        /// Whether a quick action button for clearing every flag should be displayed.
        /// Only meaningful for <c>[Flags]</c> enums.
        /// </param>
        /// <param name="enablePagination">
        /// Whether pagination may be applied when the option count exceeds the configured threshold.
        /// </param>
        /// <param name="pageSize">
        /// The maximum number of options displayed per page before pagination occurs.
        /// Values less than or equal to zero defer to the project-wide default.
        /// </param>
        /// <param name="colorKey">
        /// An optional palette key used to resolve theming for the toggle buttons.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="buttonsPerRow"/> is negative.</exception>
        public WEnumToggleButtonsAttribute(
            int buttonsPerRow = 0,
            bool showSelectAll = true,
            bool showSelectNone = true,
            bool enablePagination = true,
            int pageSize = 0,
            string colorKey = null
        )
        {
            if (buttonsPerRow < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(buttonsPerRow),
                    "Value cannot be negative."
                );
            }

            ButtonsPerRow = buttonsPerRow;
            ShowSelectAll = showSelectAll;
            ShowSelectNone = showSelectNone;
            EnablePagination = enablePagination;
            PageSize = pageSize;
            ColorKey = colorKey;
        }

        /// <summary>
        /// Gets the desired number of buttons per row.
        /// A value of zero indicates that the drawer should determine a sensible layout automatically.
        /// </summary>
        public int ButtonsPerRow { get; }

        /// <summary>
        /// Gets or sets a value indicating whether a quick action button for selecting every flag should be displayed.
        /// Only meaningful for <c>[Flags]</c> enums.
        /// </summary>
        public bool ShowSelectAll { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether a quick action button for clearing every flag should be displayed.
        /// Only meaningful for <c>[Flags]</c> enums.
        /// </summary>
        public bool ShowSelectNone { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether pagination may be applied when the option count exceeds the configured threshold.
        /// Disable when all options should always be visible regardless of their count.
        /// </summary>
        public bool EnablePagination { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of options displayed per page before pagination occurs.
        /// Values less than or equal to zero defer to the project-wide default stored in <c>UnityHelpersSettings</c>.
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// Gets or sets an optional palette key used to resolve theming for the toggle buttons.
        /// </summary>
        public string ColorKey
        {
            get => _colorKey;
            set => _colorKey = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
