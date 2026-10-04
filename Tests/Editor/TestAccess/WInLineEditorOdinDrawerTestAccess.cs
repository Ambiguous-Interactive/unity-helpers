// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System;
    using Sirenix.OdinInspector.Editor;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils;
    using WallstopStudios.UnityHelpers.Editor.Internal;
    using static WallstopStudios.UnityHelpers.Editor.CustomDrawers.WInLineEditorOdinDrawer;
    using Object = UnityEngine.Object;

    /// <summary>Provides test-side setup and inspection for WInLineEditorOdinDrawer.</summary>
    internal static class WInLineEditorOdinDrawerTestAccess
    {
        /// <summary>
        /// Clears cached editors and state. Primarily for testing purposes.
        /// </summary>
        internal static void ClearCachedState()
        {
            InLineEditorSharedTestAccess.ClearCachedState();
        }

        /// <summary>
        /// Test hook to set the foldout state for a given key.
        /// </summary>
        internal static void SetFoldoutState(string key, bool expanded)
        {
            InLineEditorShared.SetFoldoutState(key, expanded);
        }

        /// <summary>
        /// Test hook to get the foldout state for a given key.
        /// </summary>
        internal static bool GetFoldoutState(string key)
        {
            return InLineEditorSharedTestAccess.GetFoldoutState(key);
        }
    }
#endif
}
