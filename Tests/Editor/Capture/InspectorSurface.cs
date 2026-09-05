// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Capture
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Builds the visual element <see cref="EditorSurfaceCapture"/> captures for one or more real
    /// inspectors.
    ///
    /// The package's inspectors and drawers are IMGUI, and an <see cref="IMGUIContainer"/> is what
    /// lets the panel renderer draw IMGUI into the offscreen target: the container's handler runs
    /// during the panel's repaint, so the captured pixels are the shipped inspector drawing
    /// itself, not a reconstruction of it.
    ///
    /// The surface owns the <see cref="UnityEditor.Editor"/> instances it creates and destroys
    /// them on <see cref="Dispose"/>; the inspected objects stay owned by the caller.
    /// </summary>
    internal sealed class InspectorSurface : IDisposable
    {
        private const float ColumnPadding = 8f;

        /// <summary>
        /// Unity's inspector background. Neither skin exposes it as API, and a transparent
        /// surface would read back as black gaps between the inspector's own boxes.
        /// </summary>
        private static readonly Color DarkSkinBackground = new(0.2196f, 0.2196f, 0.2196f, 1f);

        private static readonly Color LightSkinBackground = new(0.7608f, 0.7608f, 0.7608f, 1f);

        private readonly List<UnityEditor.Editor> _editors = new();

        private InspectorSurface(VisualElement root)
        {
            Root = root;
        }

        /// <summary>The element to hand to <see cref="EditorSurfaceCapture.Capture"/>.</summary>
        internal VisualElement Root { get; }

        /// <summary>
        /// Lays out one inspector body per entry of <paramref name="targets"/>, side by side when
        /// there is more than one, which is how the documentation shows before/after comparisons.
        /// </summary>
        internal static InspectorSurface Create(
            List<Object> targets,
            float columnWidth,
            float labelWidth,
            bool drawHeader
        )
        {
            if (targets == null)
            {
                throw new ArgumentNullException(nameof(targets));
            }

            if (targets.Count <= 0)
            {
                throw new ArgumentException(
                    "An inspector surface needs at least one target.",
                    nameof(targets)
                );
            }

            if (columnWidth <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(columnWidth),
                    $"Inspector column width must be positive, got {columnWidth}."
                );
            }

            VisualElement root = new();
            root.style.flexDirection = FlexDirection.Row;
            root.style.backgroundColor = EditorGUIUtility.isProSkin
                ? DarkSkinBackground
                : LightSkinBackground;
            // Default stretching would make the capture as wide as the offscreen canvas instead of the inspector.
            root.style.width = columnWidth * targets.Count;
            root.style.flexGrow = 0f;
            root.style.flexShrink = 0f;
            root.style.alignSelf = Align.FlexStart;

            InspectorSurface surface = new(root);
            try
            {
                for (int index = 0; index < targets.Count; index++)
                {
                    Object target = targets[index];
                    if (target == null)
                    {
                        throw new ArgumentException(
                            $"Inspector target at index {index} is null or destroyed.",
                            nameof(targets)
                        );
                    }

                    UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(target);
                    if (editor == null)
                    {
                        throw new InvalidOperationException(
                            $"Unity produced no inspector for {target.GetType().FullName}."
                        );
                    }

                    surface._editors.Add(editor);
                    root.Add(BuildColumn(editor, columnWidth, labelWidth, drawHeader));
                }
            }
            catch (Exception)
            {
                surface.Dispose();
                throw;
            }

            return surface;
        }

        public void Dispose()
        {
            Root.Clear();
            foreach (UnityEditor.Editor editor in _editors)
            {
                if (editor != null)
                {
                    Object.DestroyImmediate(editor); // UNH-SUPPRESS: this surface owns its editors
                }
            }

            _editors.Clear();
        }

        private static VisualElement BuildColumn(
            UnityEditor.Editor editor,
            float columnWidth,
            float labelWidth,
            bool drawHeader
        )
        {
            VisualElement column = new();
            column.style.width = columnWidth;
            column.style.paddingLeft = ColumnPadding;
            column.style.paddingRight = ColumnPadding;
            column.style.paddingTop = ColumnPadding;
            column.style.paddingBottom = ColumnPadding;

            IMGUIContainer container = new(() =>
            {
                float previousLabelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = labelWidth;
                try
                {
                    if (editor == null)
                    {
                        return;
                    }

                    if (drawHeader)
                    {
                        editor.DrawHeader();
                    }

                    editor.OnInspectorGUI();
                }
                finally
                {
                    EditorGUIUtility.labelWidth = previousLabelWidth;
                }
            });
            column.Add(container);
            return column;
        }
    }
#endif
}
