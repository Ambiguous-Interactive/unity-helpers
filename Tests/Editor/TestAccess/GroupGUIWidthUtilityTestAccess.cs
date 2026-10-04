// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils
{
#if UNITY_EDITOR
    using System;
    using UnityEditor;
    using UnityEngine;
    using static WallstopStudios.UnityHelpers.Editor.Utils.GroupGUIWidthUtility;

    /// <summary>Provides test-side setup and inspection for GroupGUIWidthUtility.</summary>
    internal static class GroupGUIWidthUtilityTestAccess
    {
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        internal static void Reset()
        {
            _totalPadding = 0f;
            _totalLeftPadding = 0f;
            _totalRightPadding = 0f;
            _scopeDepth = 0;
            _isInsideWGroupPropertyDraw = false;
            _currentThemeState = null;
        }
    }
#endif
}
