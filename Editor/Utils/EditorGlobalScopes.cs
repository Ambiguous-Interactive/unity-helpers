// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils
{
#if UNITY_EDITOR
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>
    /// The IMGUI globals this package borrows, each owned by one
    /// <see cref="RestorableGlobal{T}"/> so nested drawers agree about who holds what.
    /// </summary>
    internal static class EditorGlobalScopes
    {
        internal static readonly RestorableGlobal<int> IndentLevel = new RestorableGlobal<int>(
            () => EditorGUI.indentLevel,
            value => EditorGUI.indentLevel = value
        );

        internal static readonly RestorableGlobal<float> LabelWidth = new RestorableGlobal<float>(
            () => EditorGUIUtility.labelWidth,
            value => EditorGUIUtility.labelWidth = value
        );
    }
#endif
}
