// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils.WButton
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEditor.AnimatedValues;
    using UnityEditorInternal;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Utils;
    using static WallstopStudios.UnityHelpers.Editor.Utils.WButton.WButtonGUI;

    /// <summary>Provides test-side setup and inspection for WButtonGUI.</summary>
    internal static class WButtonGUITestAccess
    {
        internal static Dictionary<WButtonGroupKey, int> GetGroupCounts()
        {
            return GroupCounts;
        }

        internal static Dictionary<WButtonGroupKey, string> GetGroupNames()
        {
            return GroupNames;
        }

        /// <summary>
        /// For testing: sets group counts with simple int keys (legacy compatibility).
        /// Creates group keys with the given draw order and empty group name.
        /// </summary>
        internal static void SetGroupCounts(Dictionary<int, int> counts)
        {
            GroupCounts.Clear();
            foreach (KeyValuePair<int, int> entry in counts)
            {
                WButtonGroupKey key = new(
                    WButtonAttribute.NoGroupPriority,
                    entry.Key,
                    null,
                    0,
                    WButtonGroupPlacement.UseGlobalSetting
                );
                GroupCounts[key] = entry.Value;
            }
        }

        /// <summary>
        /// For testing: sets group names with simple int keys (legacy compatibility).
        /// Creates group keys with the given draw order and empty group name.
        /// </summary>
        internal static void SetGroupNames(Dictionary<int, string> names)
        {
            GroupNames.Clear();
            foreach (KeyValuePair<int, string> entry in names)
            {
                WButtonGroupKey key = new(
                    WButtonAttribute.NoGroupPriority,
                    entry.Key,
                    null,
                    0,
                    WButtonGroupPlacement.UseGlobalSetting
                );
                GroupNames[key] = entry.Value;
            }
        }

        /// <summary>
        /// For testing: clears all group counts and names.
        /// </summary>
        internal static void ClearGroupData()
        {
            GroupCounts.Clear();
            GroupNames.Clear();
        }

        /// <summary>
        /// Clears conflicting draw order warnings. Used for testing.
        /// </summary>
        internal static void ClearConflictingDrawOrderWarnings()
        {
            ConflictingDrawOrderWarnings.Clear();
        }

        /// <summary>
        /// Clears conflicting group priority warnings. Used for testing.
        /// </summary>
        internal static void ClearConflictingGroupPriorityWarnings()
        {
            ConflictingGroupPriorityWarnings.Clear();
        }

        /// <summary>
        /// Clears conflicting group placement warnings. Used for testing.
        /// </summary>
        internal static void ClearConflictingGroupPlacementWarnings()
        {
            ConflictingGroupPlacementWarnings.Clear();
        }

        /// <summary>
        /// Clears the conflict warning content cache. Used for testing.
        /// </summary>
        internal static void ClearConflictWarningContentCache()
        {
            ConflictWarningTextCache.Clear();
            GroupPriorityWarningTextCache.Clear();
            GroupPlacementWarningTextCache.Clear();
        }

        /// <summary>
        /// Gets cached group placement warning text by group name. Used for testing.
        /// </summary>
        internal static bool TryGetGroupPlacementWarningText(
            string groupName,
            out string warningText
        )
        {
            if (string.IsNullOrEmpty(groupName))
            {
                warningText = null;
                return false;
            }

            return GroupPlacementWarningTextCache.TryGetValue(
                "placement_" + groupName,
                out warningText
            );
        }

        /// <summary>
        /// Gets cached group priority warning text by group name. Used for testing.
        /// </summary>
        internal static bool TryGetGroupPriorityWarningText(
            string groupName,
            out string warningText
        )
        {
            if (string.IsNullOrEmpty(groupName))
            {
                warningText = null;
                return false;
            }

            return GroupPriorityWarningTextCache.TryGetValue(
                "priority_" + groupName,
                out warningText
            );
        }

        /// <summary>
        /// Gets cached draw order warning text by group name. Used for testing.
        /// </summary>
        internal static bool TryGetDrawOrderWarningText(string groupName, out string warningText)
        {
            if (string.IsNullOrEmpty(groupName))
            {
                warningText = null;
                return false;
            }

            return ConflictWarningTextCache.TryGetValue(groupName, out warningText);
        }

        /// <summary>
        /// Legacy overload for testing compatibility.
        /// </summary>
        internal static GUIContent BuildGroupHeader(int drawOrder)
        {
            WButtonGroupKey key = new(
                WButtonAttribute.NoGroupPriority,
                drawOrder,
                null,
                0,
                WButtonGroupPlacement.UseGlobalSetting
            );
            return WButtonGUI.BuildGroupHeader(key);
        }

        internal static GUIContent BuildGroupHeader(WButtonGroupKey key)
        {
            return WButtonGUI.BuildGroupHeader(key);
        }
    }
#endif
}
