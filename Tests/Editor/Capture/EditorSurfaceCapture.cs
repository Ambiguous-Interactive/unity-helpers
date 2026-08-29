// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// UNH-SUPPRESS UNH003: this is capture INFRASTRUCTURE, not a test fixture. It owns the
// offscreen render target and readback texture for the length of a single capture and
// destroys both in a finally, which is stricter than deferring them to a fixture teardown.
namespace WallstopStudios.UnityHelpers.Tests.Editor.Capture
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Text;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.Rendering;
    using UnityEngine.UIElements;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Renders a package-owned editor surface into an offscreen render target and encodes it as a
    /// 24-bit PNG, without ever reading the desktop.
    ///
    /// The harness hosts the real shipped view in a hidden popup window, settles that window's
    /// panel layout, drives repaint and render, and reads back only the temporary target it
    /// created. It never uses a screen-pixel reader, native window capture, or programmatic skin
    /// switching, because all three read whatever the host desktop happens to be showing.
    ///
    /// Four details are load-bearing and were established by experiment in the reference
    /// implementation this is adapted from (DxMessaging pull request 473), then re-confirmed in a
    /// Direct3D11 editor here:
    ///
    /// - The panel's <c>ValidateLayout</c>, <c>Repaint</c> and <c>Render</c> are inherited, so
    ///   they must be reflected with instance, public and non-public binding flags and WITHOUT
    ///   <see cref="BindingFlags.DeclaredOnly"/>. Reflecting on Unity's internal panel API is the
    ///   one place this repository allows reflection: nothing here reflects on package types.
    /// - Three repaint/render passes are needed after layout settles. Nested scroll views realize
    ///   their content on the first, the dynamic font atlas can extend on the second, and the
    ///   third draws both settled sets. Stopping earlier yields a valid PNG with blank scroll
    ///   bodies or missing labels.
    /// - In a linear-color project a linear render target with <c>GL.sRGBWrite</c> disabled is
    ///   what matches the colors the real panel shows.
    /// - The image is cropped to the surface's own laid-out rect rather than to the canvas, so the
    ///   padding in the image comes from the surface's styling instead of slack in the canvas.
    /// </summary>
    internal static class EditorSurfaceCapture
    {
        /// <summary>PNG IHDR color type 2: truecolor, no alpha channel.</summary>
        internal const byte PngTruecolorWithoutAlpha = 2;

        /// <summary>
        /// Name given to the offscreen canvas. Tests assert nothing with this name survives a
        /// capture, which is a precise leak check rather than a fragile object-count delta.
        /// </summary>
        internal const string CanvasObjectName = "EditorSurfaceCaptureCanvas";

        /// <summary>Name given to the readback texture, for the same reason.</summary>
        internal const string ReadbackObjectName = "EditorSurfaceCaptureReadback";

        /// <summary>
        /// Byte offset of the IHDR color-type field: 8-byte signature + 4-byte length +
        /// 4-byte "IHDR" + 4-byte width + 4-byte height + 1-byte bit depth.
        /// </summary>
        private const int PngColorTypeOffset = 25;

        private const int RepaintPasses = 3;

        /// <summary>
        /// How many layout passes <see cref="SettleLayout"/> runs before giving up. Real surfaces
        /// settle in two or three; an IMGUI container that reports its height one pass after it is
        /// measured needs more than one.
        /// </summary>
        private const int MaxLayoutPasses = 8;

        private const BindingFlags InheritedInstanceMembers =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // Unity's internal panel API. These are not package members, so there is nothing to point
        // nameof at; they are named once here so a Unity rename fails in one place with a
        // diagnostic instead of everywhere with a null reference.
        private const string ValidateLayoutMethodName = "ValidateLayout";
        private const string RepaintMethodName = "Repaint";
        private const string RenderMethodName = "Render";

        /// <summary>
        /// Offscreen rendering needs a real graphics device. Continuous integration runs Unity
        /// with <c>-nographics</c>, so capture tests skip there rather than assert against a
        /// device that cannot rasterize anything.
        /// </summary>
        internal static bool IsSupported =>
            SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        /// <summary>
        /// The message a fixture should skip with when this editor cannot capture.
        /// </summary>
        internal static string UnsupportedReason =>
            "Editor surface capture needs a graphics device to rasterize into an offscreen target. "
            + $"This editor reports {SystemInfo.graphicsDeviceType}, which means it was launched "
            + "with -nographics (as continuous integration does). Run this fixture in an editor "
            + "with a real graphics device to regenerate images.";

        /// <summary>
        /// Renders <paramref name="content"/> onto a <paramref name="canvasWidth"/> by
        /// <paramref name="canvasHeight"/> offscreen canvas and writes a 24-bit PNG of the
        /// surface's own laid-out rect to <paramref name="outputPath"/>. The canvas only has to be
        /// large enough to hold the surface; the written image is cropped to the surface, so its
        /// dimensions come from the surface's layout rather than from these arguments.
        ///
        /// Every global the render touches -- the active render target and <c>GL.sRGBWrite</c> --
        /// is restored, and every object it creates is destroyed, including on the failure path.
        /// </summary>
        internal static EditorSurfaceCaptureResult Capture(
            VisualElement content,
            int canvasWidth,
            int canvasHeight,
            string outputPath
        )
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            if (canvasWidth <= 0 || canvasHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(canvasWidth),
                    $"Capture canvas must be positive, got {canvasWidth}x{canvasHeight}."
                );
            }

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                throw new ArgumentException("Capture needs an output path.", nameof(outputPath));
            }

            if (!IsSupported)
            {
                throw new InvalidOperationException(UnsupportedReason);
            }

            RenderTexture previousTarget = RenderTexture.active;
            bool previousSrgbWrite = GL.sRGBWrite;
            EditorSurfaceCaptureHostWindow host = null;
            RenderTexture target = null;
            Texture2D readback = null;
            try
            {
                host = EditorSurfaceCaptureHostWindow.Create(canvasWidth, canvasHeight);

                VisualElement root = host.rootVisualElement;
                root.style.width = canvasWidth;
                root.style.height = canvasHeight;
                root.Add(content);

                IPanel panel = root.panel;
                if (panel == null)
                {
                    throw new InvalidOperationException(
                        "The capture host window produced no panel to render."
                    );
                }

                target = new RenderTexture(
                    canvasWidth,
                    canvasHeight,
                    24,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.Linear
                )
                {
                    name = CanvasObjectName,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                if (!target.Create())
                {
                    throw new InvalidOperationException(
                        $"Could not create a {canvasWidth}x{canvasHeight} capture canvas."
                    );
                }

                GL.sRGBWrite = false;
                RenderTexture.active = target;
                GL.Clear(true, true, Color.clear);

                SettleLayout(root);
                int renderErrorCount;
                bool onlyCursorRectErrors;
                string renderErrorSummary;
                using (CaptureRenderLogRecorder recorder = new())
                {
                    for (int repaintPass = 0; repaintPass < RepaintPasses; repaintPass++)
                    {
                        InvokeInheritedPanelMethod(
                            panel,
                            RepaintMethodName,
                            new object[] { new Event { type = EventType.Repaint } }
                        );
                        InvokeInheritedPanelMethod(panel, RenderMethodName, Array.Empty<object>());
                    }

                    renderErrorCount = recorder.Errors.Count;
                    onlyCursorRectErrors = recorder.OnlyRecordedCursorRectErrors;
                    renderErrorSummary = recorder.Summary;
                }

                RectInt crop = ResolveCropRect(content, canvasWidth, canvasHeight);
                // The harness owns this texture for the length of one capture and destroys it
                // in the finally below; deferring it to a fixture teardown would hold it alive
                // across the whole fixture instead.
                readback = new Texture2D(crop.width, crop.height, TextureFormat.RGB24, false, true) // UNH-SUPPRESS UNH002
                {
                    name = ReadbackObjectName,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                readback.ReadPixels(new Rect(crop.x, crop.y, crop.width, crop.height), 0, 0, false);
                readback.Apply(false, false);

                byte[] png = readback.EncodeToPNG();
                if (png == null || png.Length <= PngColorTypeOffset)
                {
                    throw new InvalidOperationException("PNG encoding produced no usable bytes.");
                }

                byte colorType = png[PngColorTypeOffset];
                if (colorType != PngTruecolorWithoutAlpha)
                {
                    throw new InvalidOperationException(
                        $"Capture produced PNG color type {colorType}; documentation images "
                            + $"require {PngTruecolorWithoutAlpha} (truecolor without alpha)."
                    );
                }

                string directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllBytes(outputPath, png);

                return new EditorSurfaceCaptureResult(
                    outputPath,
                    crop.width,
                    crop.height,
                    png.Length,
                    colorType,
                    CountDistinctColors(readback),
                    renderErrorCount,
                    onlyCursorRectErrors,
                    renderErrorSummary,
                    EditorGUIUtility.isProSkin,
                    Application.unityVersion
                );
            }
            finally
            {
                RenderTexture.active = previousTarget;
                GL.sRGBWrite = previousSrgbWrite;

                if (readback != null)
                {
                    Object.DestroyImmediate(readback); // UNH-SUPPRESS: harness-owned, destroyed on every path
                }

                if (target != null)
                {
                    target.Release();
                    Object.DestroyImmediate(target); // UNH-SUPPRESS: harness-owned, destroyed on every path
                }

                EditorSurfaceCaptureHostWindow.CloseHost(host);
            }
        }

        /// <summary>
        /// Runs the panel's layout until it stops changing, so the capture reads the geometry a
        /// reader settles on rather than an intermediate frame. One pass is never enough: text
        /// height is only final once its width is, and an IMGUI container asks for its height
        /// during one pass and receives it in the next.
        /// </summary>
        internal static void SettleLayout(VisualElement root)
        {
            if (root == null)
            {
                return;
            }

            IPanel panel = root.panel;
            if (panel == null)
            {
                return;
            }

            string previous = null;
            for (int pass = 0; pass < MaxLayoutPasses; pass++)
            {
                InvokeInheritedPanelMethod(panel, ValidateLayoutMethodName, Array.Empty<object>());

                string current = DescribeLayout(root);
                if (string.Equals(current, previous, StringComparison.Ordinal))
                {
                    return;
                }

                previous = current;
            }
        }

        /// <summary>
        /// Invokes one of Unity's inherited panel methods by name. Declaring-type-only lookup
        /// finds nothing, because the methods live on a base panel type.
        /// </summary>
        internal static void InvokeInheritedPanelMethod(
            IPanel panel,
            string methodName,
            object[] arguments
        )
        {
            if (panel == null)
            {
                throw new ArgumentNullException(nameof(panel));
            }

            Type panelType = panel.GetType();
            Type[] argumentTypes = new Type[arguments.Length];
            for (int index = 0; index < arguments.Length; index++)
            {
                argumentTypes[index] = arguments[index].GetType();
            }

            MethodInfo method = panelType.GetMethod(
                methodName,
                InheritedInstanceMembers,
                binder: null,
                types: argumentTypes,
                modifiers: null
            );
            if (method == null)
            {
                throw new InvalidOperationException(
                    $"Panel type {panelType.FullName} exposes no '{methodName}' method taking "
                        + $"{argumentTypes.Length} argument(s) to drive the capture. Unity's "
                        + "internal panel API changed; update the capture harness."
                );
            }

            method.Invoke(panel, arguments);
        }

        /// <summary>
        /// Translates the surface's laid-out rect into render-target coordinates. UI Toolkit
        /// measures from the top-left; a render target's rows start at the bottom, so the vertical
        /// origin is the distance from the canvas bottom to the surface's bottom edge.
        /// </summary>
        private static RectInt ResolveCropRect(
            VisualElement content,
            int canvasWidth,
            int canvasHeight
        )
        {
            // Round the EDGES, then derive the size from them. Rounding the origin and the size
            // independently lets the two drift a pixel apart, and a pixel lost here is a pixel of
            // the surface clipped out of a documentation image.
            Rect bounds = content.worldBound;
            int cropX = Mathf.RoundToInt(bounds.x);
            int cropY = Mathf.RoundToInt(canvasHeight - bounds.yMax);
            int cropWidth = Mathf.RoundToInt(bounds.xMax) - cropX;
            int cropHeight = Mathf.RoundToInt(canvasHeight - bounds.y) - cropY;
            if (cropWidth <= 0 || cropHeight <= 0)
            {
                throw new InvalidOperationException(
                    $"The surface laid out to {bounds.width}x{bounds.height}, so there is nothing "
                        + "to capture. Give it an explicit size or content before capturing."
                );
            }

            // Refuse a surface that does not fit rather than clamping it into the canvas.
            // Clamping would write a silently clipped image, which is exactly the defect this
            // harness exists to avoid, produced by the tool meant to avoid it.
            if (
                cropX < 0
                || cropY < 0
                || canvasWidth < cropX + cropWidth
                || canvasHeight < cropY + cropHeight
            )
            {
                throw new InvalidOperationException(
                    $"The surface laid out to {bounds}, which does not fit inside the "
                        + $"{canvasWidth}x{canvasHeight} capture canvas. Enlarge the canvas; "
                        + "cropping it here would write a silently clipped image."
                );
            }

            return new RectInt(cropX, cropY, cropWidth, cropHeight);
        }

        private static string DescribeLayout(VisualElement root)
        {
            StringBuilder description = new();
            List<VisualElement> elements = root.Query<VisualElement>().ToList();
            for (int index = 0; index < elements.Count; index++)
            {
                Rect layout = elements[index].layout;
                description
                    .Append(layout.x)
                    .Append(',')
                    .Append(layout.y)
                    .Append(',')
                    .Append(layout.width)
                    .Append(',')
                    .Append(layout.height)
                    .Append(';');
            }

            return description.ToString();
        }

        private static int CountDistinctColors(Texture2D texture)
        {
            Color32[] pixels = texture.GetPixels32();
            HashSet<int> distinct = new();
            for (int index = 0; index < pixels.Length; index++)
            {
                Color32 pixel = pixels[index];
                distinct.Add((pixel.r << 16) | (pixel.g << 8) | pixel.b);
            }

            return distinct.Count;
        }
    }
#endif
}
