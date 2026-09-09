// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using UnityEngine;

    public enum WInLineEditorMode
    {
        UseSettings = 0,
        AlwaysExpanded = 1,
        FoldoutExpanded = 2,
        FoldoutCollapsed = 3,
    }

    /// <summary>
    /// Embeds the referenced object’s inspector directly beneath the field.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class WInLineEditorAttribute : PropertyAttribute
    {
        private const float MinInspectorHeight = 160f;
        private const float MinPreviewHeight = 40f;
        private const float DefaultMinInspectorWidth = 520f;

        public WInLineEditorMode Mode { get; }

        public float InspectorHeight { get; }

        public bool DrawObjectField { get; }

        public bool DrawHeader { get; }

        public bool DrawPreview { get; }

        public float PreviewHeight { get; }

        public bool EnableScrolling { get; }

        public float MinInspectorWidth { get; }

        public bool HasExplicitMinInspectorWidth { get; }

        public WInLineEditorAttribute(
            WInLineEditorMode mode = WInLineEditorMode.UseSettings,
            float inspectorHeight = 200f,
            bool drawPreview = false,
            float previewHeight = 64f,
            bool drawObjectField = true,
            bool drawHeader = true,
            bool enableScrolling = true,
            float minInspectorWidth = DefaultMinInspectorWidth
        )
        {
            Mode = mode;
            InspectorHeight = Mathf.Max(MinInspectorHeight, inspectorHeight);
            DrawPreview = drawPreview;
            PreviewHeight = Mathf.Max(MinPreviewHeight, previewHeight);
            DrawObjectField = drawObjectField;
            DrawHeader = drawHeader;
            EnableScrolling = enableScrolling;
            float sanitizedMinWidth = Mathf.Max(0f, minInspectorWidth);
            HasExplicitMinInspectorWidth = !Mathf.Approximately(
                sanitizedMinWidth,
                DefaultMinInspectorWidth
            );
            MinInspectorWidth = sanitizedMinWidth;
        }
    }
}
