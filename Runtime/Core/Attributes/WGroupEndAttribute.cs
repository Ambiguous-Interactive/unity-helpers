// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Terminates automatic member inclusion for the active <see cref="WGroupAttribute"/> instances, letting you resume the normal inspector flow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Important:</b> Place <see cref="WGroupEndAttribute"/> on the <b>last field you want included</b> in the group.
    /// The field with this attribute IS included in the group, and then the group closes for subsequent fields.
    /// </para>
    /// <para>
    /// The bare form closes <b>every</b> open group. Name the groups you want closed when others should stay open,
    /// which is the form to reach for whenever a type declares more than one group.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// [WGroup(\"Stats\", autoIncludeCount: WGroupAttribute.InfiniteAutoInclude)]
    /// public int health;
    ///
    /// public int stamina;
    ///
    /// [WGroupEnd(\"Stats\")]
    /// public float luck;        // Included in \"Stats\" group, then group closes
    ///
    /// public int gold;          // NOT in \"Stats\" group - comes after WGroupEnd
    /// </code>
    /// </example>
    // Fields only, and that does NOT exclude a property whose data is serialized. Write
    // [field: WGroup(...)] on an auto-property and the attribute lands on the compiler-generated
    // backing field -- which is a field, and is the member Unity serializes -- so it groups exactly
    // like one. WGroupLayoutBuilderTests.GroupingWorksThroughABackingFieldAttribute pins that.
    // What Field refuses is a property Unity does not serialize, which the layout has no
    // SerializedProperty path to draw and so could only ever silently do nothing.
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
    public sealed class WGroupEndAttribute : Attribute
    {
        /// <summary>
        /// Creates a new end marker optionally targeting one or more specific groups.
        /// </summary>
        /// <param name="groupNames">
        /// Explicit group keys to close. When omitted, the attribute closes every group that is currently open, whichever member opened it.
        /// </param>
        public WGroupEndAttribute(params string[] groupNames)
        {
            if (groupNames == null || groupNames.Length == 0)
            {
                GroupNames = Array.Empty<string>();
                return;
            }

            string[] normalized = new string[groupNames.Length];
            for (int index = 0; index < groupNames.Length; index++)
            {
                string name = groupNames[index];
                normalized[index] = string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
            }

            GroupNames = normalized;
        }

        /// <summary>
        /// Gets the normalized group names that should stop auto inclusion. An empty collection instructs the drawer to close all active groups.
        /// </summary>
        public IReadOnlyList<string> GroupNames { get; }
    }
}
