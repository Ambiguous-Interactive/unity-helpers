// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using UnityEditor;
    using UnityEditor.Toolbars;
    using UnityEngine;
    using UnityEngine.UIElements;

    [EditorToolbarElement(ElementId, typeof(SceneView))]
    internal sealed class ValidationToolbarButton : EditorToolbarButton
    {
        internal const string ElementId = "Sentinel/Status";

        public ValidationToolbarButton()
        {
            icon = EditorGUIUtility.IconContent("console.warnicon.sml").image as Texture2D;
            clicked += ValidationWindow.Open;
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                ValidationStatusSurfaces.StatusChanged += Refresh;
                Refresh();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
                ValidationStatusSurfaces.StatusChanged -= Refresh
            );
        }

        private void Refresh()
        {
            text = ValidationStatusSurfaces.Badge;
            tooltip = text + " · Open validation issues";
        }
    }
#endif
}
