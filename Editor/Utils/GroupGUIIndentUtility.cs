// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils
{
#if UNITY_EDITOR
    using System;
    using UnityEditor;
    using UnityEngine;

    internal static class GroupGUIIndentUtility
    {
        internal static void ExecuteWithIndentCompensation(Action drawAction)
        {
            if (drawAction == null)
            {
                return;
            }

            using (IndentLevelScope.Indent(-1))
            {
                drawAction();
            }
        }
    }
#endif
}
