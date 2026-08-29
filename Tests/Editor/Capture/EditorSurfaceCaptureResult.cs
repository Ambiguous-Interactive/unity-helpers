// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Capture
{
#if UNITY_EDITOR
    /// <summary>
    /// What a single <see cref="EditorSurfaceCapture.Capture"/> produced. Everything a caller
    /// needs to prove the image is real is here, so a test never has to re-open the file to find
    /// out whether the panel actually drew.
    /// </summary>
    internal readonly struct EditorSurfaceCaptureResult
    {
        internal EditorSurfaceCaptureResult(
            string outputPath,
            int width,
            int height,
            int byteCount,
            byte pngColorType,
            int distinctColorCount,
            int renderErrorCount,
            bool onlyCursorRectErrors,
            string renderErrorSummary,
            bool isProSkin,
            string unityVersion
        )
        {
            OutputPath = outputPath;
            Width = width;
            Height = height;
            ByteCount = byteCount;
            PngColorType = pngColorType;
            DistinctColorCount = distinctColorCount;
            RenderErrorCount = renderErrorCount;
            OnlyCursorRectErrors = onlyCursorRectErrors;
            RenderErrorSummary = renderErrorSummary;
            IsProSkin = isProSkin;
            UnityVersion = unityVersion;
        }

        internal string OutputPath { get; }

        internal int Width { get; }

        internal int Height { get; }

        internal int ByteCount { get; }

        internal byte PngColorType { get; }

        /// <summary>
        /// A cleared target and a rendered one are both valid PNGs. A cleared one has exactly one
        /// distinct color, so this is the cheap proof that the panel drew something.
        /// </summary>
        internal int DistinctColorCount { get; }

        /// <summary>
        /// How many errors Unity logged while this frame rendered. Reported rather than hidden:
        /// driving a panel without an editor view provokes one benign message, and a caller has
        /// to be able to tell that one from a surface that failed to draw.
        /// </summary>
        internal int RenderErrorCount { get; }

        /// <summary>
        /// True when every error logged during the render was the benign cursor-rect one
        /// (<see cref="CaptureRenderLogRecorder.CursorRectWithoutView"/>), including when there
        /// was none.
        /// </summary>
        internal bool OnlyCursorRectErrors { get; }

        /// <summary>Every error logged during the render, joined, for a failure message.</summary>
        internal string RenderErrorSummary { get; }

        /// <summary>
        /// Records the host skin as artifact metadata. The harness never changes the developer's
        /// skin preference, so this reports what the image was rendered against.
        /// </summary>
        internal bool IsProSkin { get; }

        internal string UnityVersion { get; }

        public override string ToString()
        {
            return $"{OutputPath} {Width}x{Height} bytes={ByteCount} pngColorType={PngColorType} "
                + $"distinctColors={DistinctColorCount} "
                + $"renderErrors={RenderErrorCount} isProSkin={IsProSkin} "
                + $"unity={UnityVersion}";
        }
    }
#endif
}
