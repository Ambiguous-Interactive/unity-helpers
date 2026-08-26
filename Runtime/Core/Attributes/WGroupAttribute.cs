// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;

    /// <summary>
    /// Declares a boxed inspector grouping (similar to Odin's <c>BoxGroup</c>) that can automatically sweep subsequent members into the same visual section.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use <see cref="WGroupAttribute"/> when you want to present related fields together without manually repeating the attribute on each member.
    /// The <paramref name="autoIncludeCount"/> parameter determines how many following serialized members are automatically captured.
    /// Combine it with <see cref="WGroupEndAttribute"/> to stop inclusion early or skip particular fields.
    /// </para>
    /// <para>
    /// An unattributed member joins the group whose <see cref="WGroupAttribute"/> the reader passed most recently, so re-declaring an
    /// earlier group resumes capturing into it. When that group's budget runs out, capture falls back to the next most recent group that
    /// is still open.
    /// </para>
    /// <para>
    /// Groups can be toggled collapsible, assigned theme colors via palette keys, and rendered without headers for lightweight inline layouts.
    /// </para>
    /// <para>
    /// Groups can be nested inside other groups by specifying a <c>parentGroup</c> parameter. The parent group must be declared
    /// before or on the same property as the child group. Nested groups are rendered indented within their parent's visual container.
    /// </para>
    /// </remarks>
    /// <example>
    /// Collapsible box group with auto inclusion:
    /// <code>
    /// public sealed class WeaponStats : MonoBehaviour
    /// {
    ///     [WGroup("Damage", displayName: "Damage Settings", autoIncludeCount: 2, collapsible: true)]
    ///     public int lightAttackDamage;
    ///
    ///     public int heavyAttackDamage;
    ///     public float critMultiplier;
    ///
    ///     [WGroup("Damage"), WGroupEnd]
    ///     public AnimationCurve falloff;
    /// }
    /// </code>
    /// Nested groups example:
    /// <code>
    /// public sealed class CharacterData : MonoBehaviour
    /// {
    ///     [WGroup("outer", "Character")]
    ///     public string characterName;
    ///
    ///     [WGroup("inner", "Stats", parentGroup: "outer")]  // Nested inside "outer"
    ///     public int level;
    ///     public int experience;
    /// }
    /// </code>
    /// </example>
    // Fields only, and that does NOT exclude a property whose data is serialized. Write
    // [field: WGroup(...)] on an auto-property and the attribute lands on the compiler-generated
    // backing field -- which is a field, and is the member Unity serializes -- so it groups exactly
    // like one. WGroupLayoutBuilderTests.GroupingWorksThroughABackingFieldAttribute pins that.
    // What Field refuses is a property Unity does not serialize, which the layout has no
    // SerializedProperty path to draw and so could only ever silently do nothing.
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
    public sealed class WGroupAttribute : Attribute
    {
        /// <summary>
        /// Sentinel value instructing the drawer to keep auto including members until a matching <see cref="WGroupEndAttribute"/> is reached.
        /// </summary>
        public const int InfiniteAutoInclude = -1;

        /// <summary>
        /// Sentinel value that tells the drawer to fall back to the global auto include count defined in the Unity Helpers settings asset.
        /// </summary>
        public const int UseGlobalAutoInclude = -2;

        /// <summary>
        /// Creates a new grouped inspector section.
        /// </summary>
        /// <param name="groupName">Unique key that ties <see cref="WGroupAttribute"/> and <see cref="WGroupEndAttribute"/> entries together.</param>
        /// <param name="displayName">Optional heading shown in the inspector. Defaults to <paramref name="groupName"/>.</param>
        /// <param name="autoIncludeCount">
        /// Number of serialized members after the annotated field that should automatically join the group.
        /// Use <see cref="InfiniteAutoInclude"/> to keep including until a <see cref="WGroupEndAttribute"/> is hit.
        /// </param>
        /// <param name="collapsible">Set to <see langword="true"/> to draw the box with a foldout.</param>
        /// <param name="startCollapsed">When collapsible, controls whether the group starts closed.</param>
        /// <param name="hideHeader">Set to <see langword="true"/> to draw the group body without the title bar.</param>
        /// <param name="parentGroup">
        /// Optional name of another group that this group should be nested inside.
        /// The parent group must be declared before or on the same property as this group.
        /// </param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="groupName"/> is null or whitespace.</exception>
        public WGroupAttribute(
            string groupName,
            string displayName = null,
            int autoIncludeCount = UseGlobalAutoInclude,
            bool collapsible = false,
            bool startCollapsed = false,
            bool hideHeader = false,
            string parentGroup = null
        )
        {
            if (string.IsNullOrWhiteSpace(groupName))
            {
                throw new ArgumentException(
                    "Group name cannot be null or whitespace.",
                    nameof(groupName)
                );
            }

            GroupName = groupName.Trim();
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? GroupName : displayName.Trim();
            AutoIncludeCount = NormalizeAutoIncludeCount(autoIncludeCount);
            Collapsible = collapsible;
            if (startCollapsed)
            {
                CollapseBehavior = WGroupCollapseBehavior.ForceCollapsed;
            }
            HideHeader = hideHeader;
            ParentGroup = string.IsNullOrWhiteSpace(parentGroup) ? null : parentGroup.Trim();
        }

        /// <summary>
        /// Identifier shared between the start and end attributes.
        /// </summary>
        public string GroupName { get; }

        /// <summary>
        /// Human-readable title drawn in the inspector (defaults to <see cref="GroupName"/>).
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Number of trailing serialized members automatically swept into the group.
        /// </summary>
        public int AutoIncludeCount { get; }

        /// <summary>
        /// Gets a value indicating whether the group can be collapsed with a foldout toggle.
        /// </summary>
        public bool Collapsible { get; }

        /// <summary>
        /// Gets or sets how the attribute resolves its initial collapse state.
        /// </summary>
        public WGroupCollapseBehavior CollapseBehavior { get; set; } =
            WGroupCollapseBehavior.UseProjectSetting;

        /// <summary>
        /// Gets a value indicating whether a collapsible group should start closed.
        /// </summary>
        public bool StartCollapsed
        {
            get { return CollapseBehavior == WGroupCollapseBehavior.ForceCollapsed; }
        }

        /// <summary>
        /// Set to <see langword="true"/> to hide the header while still wrapping the grouped fields inside the styled container.
        /// </summary>
        public bool HideHeader { get; }

        /// <summary>
        /// Gets the name of the parent group that this group should be nested inside, or <see langword="null"/> if this is a top-level group.
        /// </summary>
        public string ParentGroup { get; }

        private static int NormalizeAutoIncludeCount(int autoIncludeCount)
        {
            if (autoIncludeCount < InfiniteAutoInclude)
            {
                return UseGlobalAutoInclude;
            }

            if (autoIncludeCount == InfiniteAutoInclude)
            {
                return InfiniteAutoInclude;
            }

            return autoIncludeCount < 0 ? UseGlobalAutoInclude : autoIncludeCount;
        }

        /// <summary>
        /// Represents how collapsible groups determine their default foldout state.
        /// </summary>
        public enum WGroupCollapseBehavior
        {
            /// <summary>
            /// Uses the Unity Helpers project setting to decide whether the header starts collapsed.
            /// </summary>
            UseProjectSetting = 0,

            /// <summary>
            /// Forces the header to start expanded regardless of project defaults.
            /// </summary>
            ForceExpanded = 1,

            /// <summary>
            /// Forces the header to start collapsed regardless of project defaults.
            /// </summary>
            ForceCollapsed = 2,
        }
    }
}
