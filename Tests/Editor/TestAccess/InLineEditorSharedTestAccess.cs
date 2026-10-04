// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Editor.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using static WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils.InLineEditorShared;
    using Object = UnityEngine.Object;

    /// <summary>Provides test-side setup and inspection for InLineEditorShared.</summary>
    internal static class InLineEditorSharedTestAccess
    {
        /// <summary>
        /// Clears all cached state. Primarily for testing purposes.
        /// </summary>
        internal static void ClearCachedState()
        {
            ClearCache();
        }

        /// <summary>
        /// Test hook to set the foldout state for a given key.
        /// </summary>
        /// <param name="key">The foldout key.</param>
        /// <param name="expanded">Whether the foldout should be expanded.</param>
        internal static void SetFoldoutState(string key, bool expanded)
        {
            if (!string.IsNullOrEmpty(key))
            {
                FoldoutStates[key] = expanded;
            }
        }

        /// <summary>
        /// Test hook to get the foldout state for a given key.
        /// </summary>
        /// <param name="key">The foldout key.</param>
        /// <returns>True if expanded; false otherwise.</returns>
        internal static bool GetFoldoutState(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            return FoldoutStates.TryGetValue(key, out bool value) && value;
        }

        /// <summary>
        /// Test hook to get the number of cached editors.
        /// </summary>
        /// <returns>The number of cached editors.</returns>
        internal static int GetEditorCacheCount()
        {
            return EditorCache.Count;
        }

        /// <summary>
        /// Test hook to get the number of cached foldout states.
        /// </summary>
        /// <returns>The number of cached foldout states.</returns>
        internal static int GetFoldoutStateCacheCount()
        {
            return FoldoutStates.Count;
        }

        /// <summary>
        /// Test hook to get the number of cached scroll positions.
        /// </summary>
        /// <returns>The number of cached scroll positions.</returns>
        internal static int GetScrollPositionCacheCount()
        {
            return ScrollPositions.Count;
        }
    }
#endif
}
