// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Capture
{
#if UNITY_EDITOR
    using System;

    /// <summary>
    /// One documentation image the harness can regenerate: which inspectors to draw, how wide to
    /// draw them, and where under <c>docs/images/</c> the result belongs.
    /// </summary>
    internal readonly struct DocumentationImage
    {
        internal DocumentationImage(
            string relativePath,
            Type[] targetTypes,
            float columnWidth,
            float labelWidth,
            int canvasWidth,
            int canvasHeight
        )
        {
            RelativePath = relativePath;
            TargetTypes = targetTypes;
            ColumnWidth = columnWidth;
            LabelWidth = labelWidth;
            CanvasWidth = canvasWidth;
            CanvasHeight = canvasHeight;
        }

        /// <summary>
        /// Path under <c>docs/images/</c>, forward-slashed, for example
        /// <c>inspector/buttons/player-debug.png</c>.
        /// </summary>
        internal string RelativePath { get; }

        /// <summary>
        /// The objects to inspect, in the order they are drawn. More than one produces the
        /// side-by-side comparison the display-name section of the guide shows.
        /// </summary>
        internal Type[] TargetTypes { get; }

        internal float ColumnWidth { get; }

        internal float LabelWidth { get; }

        /// <summary>
        /// The offscreen canvas the surface is drawn onto. It only has to be big enough to hold
        /// the surface: the image is cropped to the surface, and a surface that does not fit is
        /// refused rather than clipped.
        /// </summary>
        internal int CanvasWidth { get; }

        internal int CanvasHeight { get; }

        /// <summary>The file name without directories, for example <c>player-debug.png</c>.</summary>
        internal string FileName
        {
            get
            {
                int separator = RelativePath.LastIndexOf('/');
                return separator < 0 ? RelativePath : RelativePath.Substring(separator + 1);
            }
        }

        public override string ToString()
        {
            return RelativePath;
        }
    }
#endif
}
