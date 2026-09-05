// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using UnityEditor;
    using UnityEditor.Overlays;

    [Overlay(typeof(SceneView), "Sentinel Toolbar", true)]
    internal sealed class ValidationToolbarOverlay : ToolbarOverlay
    {
        public ValidationToolbarOverlay()
            : base(ValidationToolbarButton.ElementId) { }
    }
#endif
}
