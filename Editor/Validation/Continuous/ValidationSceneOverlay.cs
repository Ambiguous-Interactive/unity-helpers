// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using UnityEditor;
    using UnityEditor.Overlays;
    using UnityEngine.UIElements;

    [Overlay(typeof(SceneView), "Sentinel", true)]
    internal sealed class ValidationSceneOverlay : Overlay, ITransientOverlay
    {
        /// <inheritdoc />
        public bool visible => ValidationPreferences.Enabled;

        /// <inheritdoc />
        public override VisualElement CreatePanelContent() =>
            ValidationStatusSurfaces.CreatePanel();
    }
#endif
}
