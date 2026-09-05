// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// Unity 2021.2 added these APIs; host references are 2021.1. Signatures only, never shipped.
// https://docs.unity.cn/2021.3/Documentation/Manual/overlays-custom.html
namespace UnityEditor.Overlays
{
    using System;
    using UnityEngine.UIElements;

    public sealed class OverlayAttribute : Attribute
    {
        public OverlayAttribute(
            Type editorWindowType,
            string displayName,
            bool defaultDisplay = false
        ) { }
    }

    public abstract class Overlay
    {
        public abstract VisualElement CreatePanelContent();
    }

    public abstract class ToolbarOverlay : Overlay
    {
        protected ToolbarOverlay(params string[] ids) { }

        public override VisualElement CreatePanelContent() => null;
    }
}

namespace UnityEditor.Toolbars
{
    using System;
    using UnityEngine;

    public sealed class EditorToolbarElementAttribute : Attribute
    {
        public EditorToolbarElementAttribute(string id, params Type[] targetWindows) { }
    }

    public class EditorToolbarButton : UnityEditor.UIElements.ToolbarButton
    {
        public Texture2D icon { get; set; }
    }
}
