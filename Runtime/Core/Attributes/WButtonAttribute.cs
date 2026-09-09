// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;

    /// <summary>
    /// Exposes a parameterless method as a clickable inspector button for rapid authoring tools, debug hooks, or content workflows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Apply <see cref="WButtonAttribute"/> to any method on a <see cref="UnityEngine.Object"/> derived type to surface it in the inspector.
    /// You can override the label, group related buttons, and control the ordering so that the editor layout remains predictable.
    /// </para>
    /// <para>
    /// The attribute never runs in player builds—methods execute only inside the Unity Editor when the button is pressed—making it safe for destructive
    /// utilities such as spawning test content, clearing caches, or invoking validation passes.
    /// </para>
    /// <para>
    /// <b>Group Priority and Placement:</b> Use <see cref="GroupPriority"/> to control the render order of button groups within a placement section
    /// (lower values render first). Use <see cref="GroupPlacement"/> to override where a group renders (top or bottom) independent of the global setting.
    /// When multiple buttons in the same group specify different values for these properties, the first declared button's values are used and a warning
    /// is displayed in the inspector.
    /// </para>
    /// </remarks>
    /// <example>
    /// Simple editor utility buttons:
    /// <code>
    /// public sealed class EnemySpawner : MonoBehaviour
    /// {
    ///     [WButton("Rebuild Spawn Points")]
    ///     private void Rebuild()
    ///     {
    ///         // expensive setup logic omitted
    ///     }
    ///
    ///     [WButton(drawOrder: 1, groupName: "Debug")]
    ///     private void PrintSpawnCount()
    ///     {
    ///         Debug.Log(spawnPoints.Count);
    ///     }
    /// }
    /// </code>
    /// Grouped buttons with priority and placement overrides:
    /// <code>
    /// public sealed class QuestAuthoringTool : ScriptableObject
    /// {
    ///     // This group renders at the top regardless of global settings, with priority 0 (renders first)
    ///     [WButton("Generate IDs", groupName: "Authoring", groupPriority: 0, groupPlacement: WButtonGroupPlacement.Top)]
    ///     private void GenerateIds() { }
    ///
    ///     // This group renders at the bottom, with priority 10 (renders after priority 0 groups in bottom section)
    ///     [WButton("Submit", groupName: "Debug", groupPriority: 10, groupPlacement: WButtonGroupPlacement.Bottom)]
    ///     private void SubmitToServer() { }
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class WButtonAttribute : Attribute
    {
        public const int UseGlobalHistory = -1;

        /// <summary>
        /// Sentinel value indicating no explicit group priority was set.
        /// Groups without explicit priority sort after groups with explicit priorities.
        /// </summary>
        public const int NoGroupPriority = int.MaxValue;

        /// <summary>
        /// Explicit label override for the button. Falls back to the method name when null.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Controls sorting within a group. Lower values render first within the group.
        /// Buttons with the same draw order but different group names render as separate groups.
        /// </summary>
        public int DrawOrder { get; }

        /// <summary>
        /// Optional override for the number of results retained. Negative values defer to the global setting.
        /// </summary>
        public int HistoryCapacity { get; }

        /// <summary>
        /// Optional custom color key used to resolve palette-based styling.
        /// </summary>
        public string ColorKey { get; }

        /// <summary>
        /// Optional group name for organizing buttons. Buttons with the same group name
        /// are rendered together. Groups render in the order determined by <see cref="GroupPriority"/>
        /// and then by declaration order.
        /// </summary>
        public string GroupName { get; }

        /// <summary>
        /// Controls the render order of this button's group within its placement section.
        /// Lower values render first. The first declared button in a group sets the canonical
        /// priority for the entire group; conflicting values from other buttons in the same group
        /// are ignored with a warning displayed in the inspector.
        /// Default is <see cref="NoGroupPriority"/>, which sorts after groups with explicit priorities.
        /// Only applies to buttons with a <see cref="GroupName"/>; ungrouped buttons ignore this value.
        /// </summary>
        public int GroupPriority { get; }

        /// <summary>
        /// Controls whether this button's group renders at the top or bottom of the inspector,
        /// overriding the global Unity Helpers setting. The first declared button in a group sets
        /// the canonical placement for the entire group; conflicting values from other buttons in
        /// the same group are ignored with a warning displayed in the inspector.
        /// Default is <see cref="WButtonGroupPlacement.UseGlobalSetting"/>.
        /// Only applies to buttons with a <see cref="GroupName"/>; ungrouped buttons ignore this value.
        /// </summary>
        public WButtonGroupPlacement GroupPlacement { get; }

        /// <summary>
        /// Legacy alias for <see cref="ColorKey"/> to maintain backwards compatibility.
        /// </summary>
        [Obsolete("Use ColorKey instead.")]
        public string Priority => ColorKey;

        public WButtonAttribute(
            string displayName = null,
            int drawOrder = 0,
            int historyCapacity = UseGlobalHistory,
            string colorKey = null,
            string groupName = null,
            int groupPriority = NoGroupPriority,
            WButtonGroupPlacement groupPlacement = WButtonGroupPlacement.UseGlobalSetting
        )
        {
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
            DrawOrder = drawOrder;
            HistoryCapacity = historyCapacity < 0 ? UseGlobalHistory : historyCapacity;
            ColorKey = string.IsNullOrWhiteSpace(colorKey) ? null : colorKey.Trim();
            GroupName = string.IsNullOrWhiteSpace(groupName) ? null : groupName.Trim();
            GroupPriority = groupPriority;
            GroupPlacement = groupPlacement;
        }
    }
}
