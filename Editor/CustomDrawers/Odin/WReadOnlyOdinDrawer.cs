// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector.Editor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Odin Inspector attribute drawer for <see cref="WReadOnlyAttribute"/>.
    /// Makes fields non-editable (display only) in the inspector.
    /// </summary>
    /// <remarks>
    /// This drawer ensures WReadOnly works correctly when Odin Inspector is installed
    /// and classes derive from SerializedMonoBehaviour or SerializedScriptableObject,
    /// where Unity's standard PropertyDrawer system is bypassed.
    /// </remarks>
    public sealed class WReadOnlyOdinDrawer : OdinAttributeDrawer<WReadOnlyAttribute>
    {
        protected override void DrawPropertyLayout(GUIContent label)
        {
            bool previousEnabled = GUI.enabled;
            try
            {
                GUI.enabled = false;
                CallNextDrawer(label);
            }
            finally
            {
                GUI.enabled = previousEnabled;
            }
        }
    }
#endif
}
