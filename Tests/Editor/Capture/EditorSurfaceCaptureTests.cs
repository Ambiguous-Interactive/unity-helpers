// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Capture
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.Rendering;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// Contract tests for the capture harness itself. A green test over a blank image is the
    /// failure mode the whole design guards against, so these assert on the pixels, on the crop,
    /// and on every global and object the harness touches -- including on the failure path.
    /// </summary>
    [TestFixture]
    [Category("Editor")]
    public sealed class EditorSurfaceCaptureTests : CommonTestBase
    {
        private const int CanvasWidth = 400;
        private const int CanvasHeight = 300;
        private const int SurfaceWidth = 180;
        private const int SurfaceHeight = 120;

        private const string ProbeErrorMessage = "capture-recorder-probe";

        private string _outputDirectory;

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            _outputDirectory = Path.Combine(
                Path.GetTempPath(),
                "unity-helpers-editor-surface-capture",
                Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(_outputDirectory);
        }

        [TearDown]
        public override void TearDown()
        {
            EditorSurfaceCaptureHostWindow.CloseLeakedHosts();
            if (!string.IsNullOrEmpty(_outputDirectory) && Directory.Exists(_outputDirectory))
            {
                Directory.Delete(_outputDirectory, true);
            }

            base.TearDown();
        }

        [Test]
        public void IsSupportedTracksTheGraphicsDevice()
        {
            Assert.AreEqual(
                SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null,
                EditorSurfaceCapture.IsSupported,
                "Capture support must be decided by the graphics device, because an editor "
                    + "launched with -nographics cannot rasterize into an offscreen target."
            );
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(EditorSurfaceCapture.UnsupportedReason),
                "An unsupported editor must explain itself, or the skip reads as a silent pass."
            );
        }

        [Test]
        public void CaptureWritesTruecolorPngWithoutAlpha()
        {
            SkipWithoutGraphicsDevice();

            EditorSurfaceCaptureResult result = CaptureLabelSurface(
                "Truecolor",
                nameof(CaptureWritesTruecolorPngWithoutAlpha)
            );

            Assert.AreEqual(
                EditorSurfaceCapture.PngTruecolorWithoutAlpha,
                result.PngColorType,
                "Documentation images must be truecolor without an alpha channel."
            );
            Assert.IsTrue(File.Exists(result.OutputPath), "Capture must write the file it names.");

            byte[] written = File.ReadAllBytes(result.OutputPath);
            Assert.AreEqual(
                result.ByteCount,
                written.Length,
                "The reported byte count must match the file on disk."
            );
            Assert.AreEqual(
                EditorSurfaceCapture.PngTruecolorWithoutAlpha,
                written[25],
                "The PNG header on disk must declare truecolor without alpha."
            );
        }

        [Test]
        public void CaptureProducesNonBlankFrame()
        {
            SkipWithoutGraphicsDevice();

            EditorSurfaceCaptureResult result = CaptureLabelSurface(
                "Rendered content",
                nameof(CaptureProducesNonBlankFrame)
            );

            Assert.IsTrue(
                1 < result.DistinctColorCount,
                "A cleared target has exactly one distinct color. "
                    + $"This capture reported {result.DistinctColorCount}, so the panel drew "
                    + "nothing: the image is blank."
            );

            Texture2D decoded = DecodePng(result.OutputPath);
            Assert.IsTrue(
                1 < CountDistinctColors(decoded),
                "The written PNG must contain more than one color, not just the in-memory frame."
            );
        }

        [Test]
        public void CaptureCropsToSurfaceRatherThanCanvas()
        {
            SkipWithoutGraphicsDevice();

            EditorSurfaceCaptureResult result = Capture(
                BuildSolidSurface(Color.red),
                nameof(CaptureCropsToSurfaceRatherThanCanvas)
            );

            Assert.AreEqual(
                SurfaceWidth,
                result.Width,
                "The image width must come from the surface's layout, not the canvas."
            );
            Assert.AreEqual(
                SurfaceHeight,
                result.Height,
                "The image height must come from the surface's layout, not the canvas."
            );
            Assert.IsTrue(
                result.Width < CanvasWidth && result.Height < CanvasHeight,
                "This test only proves something while the canvas is larger than the surface."
            );

            Texture2D decoded = DecodePng(result.OutputPath);
            Assert.AreEqual(SurfaceWidth, decoded.width, "The file must be cropped too.");
            Assert.AreEqual(SurfaceHeight, decoded.height, "The file must be cropped too.");
        }

        [Test]
        public void CaptureExcludesWindowChrome()
        {
            SkipWithoutGraphicsDevice();

            EditorSurfaceCaptureResult flush = CaptureSolidSurfaceAtOffset(
                0f,
                0f,
                CanvasWidth,
                CanvasHeight,
                nameof(CaptureExcludesWindowChrome) + "Flush"
            );

            Texture2D decoded = DecodePng(flush.OutputPath);
            Assert.AreEqual(
                1,
                CountDistinctColors(decoded),
                "A solid-colored surface must read back as exactly one color. More than one "
                    + "means the crop is letting the host window's own pixels into the image."
            );

            EditorSurfaceCaptureResult offset = CaptureSolidSurfaceAtOffset(
                57f,
                41f,
                CanvasWidth + 200,
                CanvasHeight + 200,
                nameof(CaptureExcludesWindowChrome) + "Offset"
            );

            Assert.AreEqual(
                File.ReadAllBytes(flush.OutputPath),
                File.ReadAllBytes(offset.OutputPath),
                "The same surface at a different offset in a larger canvas produced different "
                    + "bytes, so the crop is not tracking the surface's own laid-out rect."
            );
        }

        [Test]
        public void DistinctSurfacesProduceDistinctPngBytes()
        {
            SkipWithoutGraphicsDevice();

            EditorSurfaceCaptureResult first = CaptureLabelSurface(
                "First surface",
                nameof(DistinctSurfacesProduceDistinctPngBytes) + "First"
            );
            EditorSurfaceCaptureResult second = CaptureLabelSurface(
                "Second surface, different text",
                nameof(DistinctSurfacesProduceDistinctPngBytes) + "Second"
            );

            Assert.AreNotEqual(
                File.ReadAllBytes(first.OutputPath),
                File.ReadAllBytes(second.OutputPath),
                "Two different surfaces encoded to identical bytes, which means the capture is "
                    + "not reading the surface it was handed."
            );
        }

        [Test]
        public void CaptureRestoresRenderTargetAndSRgbWrite()
        {
            SkipWithoutGraphicsDevice();

            RenderTexture sentinel = Track(new RenderTexture(8, 8, 0));
            sentinel.Create();
            RenderTexture previousTarget = RenderTexture.active;
            bool previousSrgbWrite = GL.sRGBWrite;
            try
            {
                RenderTexture.active = sentinel;
                GL.sRGBWrite = true;

                CaptureLabelSurface("Globals", nameof(CaptureRestoresRenderTargetAndSRgbWrite));

                Assert.AreSame(
                    sentinel,
                    RenderTexture.active,
                    "Capture must restore the render target it borrowed."
                );
                Assert.IsTrue(GL.sRGBWrite, "Capture must restore GL.sRGBWrite.");
            }
            finally
            {
                RenderTexture.active = previousTarget;
                GL.sRGBWrite = previousSrgbWrite;
            }
        }

        [Test]
        public void CaptureReportsNoRenderErrorsForAPlainSurface()
        {
            SkipWithoutGraphicsDevice();

            EditorSurfaceCaptureResult result = CaptureLabelSurface(
                "Render errors",
                nameof(CaptureReportsNoRenderErrorsForAPlainSurface)
            );

            Assert.AreEqual(
                0,
                result.RenderErrorCount,
                "A surface with no cursor-rect drawer must render without logging anything. "
                    + $"This one logged: {result.RenderErrorSummary}"
            );
            Assert.IsTrue(
                result.OnlyCursorRectErrors,
                "With no errors at all, the cursor-rect classification must still hold."
            );
        }

        [Test]
        public void CaptureUnsubscribesItsRenderLogRecorder()
        {
            SkipWithoutGraphicsDevice();

            CaptureLabelSurface("Recorder", nameof(CaptureUnsubscribesItsRenderLogRecorder));

            using CaptureRenderLogRecorder probe = new();
            Debug.LogError(ProbeErrorMessage);
            LogAssert.Expect(LogType.Error, ProbeErrorMessage);

            Assert.AreEqual(
                1,
                probe.Errors.Count,
                "Exactly one recorder must observe this error. A capture that left its recorder "
                    + "subscribed would keep collecting into an object nobody reads."
            );
        }

        [Test]
        public void CaptureRestoresGlobalsWhenTheSurfaceDoesNotFit()
        {
            SkipWithoutGraphicsDevice();

            RenderTexture previousTarget = RenderTexture.active;
            bool previousSrgbWrite = GL.sRGBWrite;

            Assert.Throws<InvalidOperationException>(
                () =>
                    EditorSurfaceCapture.Capture(
                        BuildSolidSurface(Color.red, CanvasWidth * 2, CanvasHeight * 2),
                        CanvasWidth,
                        CanvasHeight,
                        OutputPath(nameof(CaptureRestoresGlobalsWhenTheSurfaceDoesNotFit))
                    ),
                "A surface larger than the canvas must be refused, not silently clipped."
            );

            Assert.AreSame(
                previousTarget,
                RenderTexture.active,
                "The failure path must restore the render target too."
            );
            Assert.AreEqual(
                previousSrgbWrite,
                GL.sRGBWrite,
                "The failure path must restore GL.sRGBWrite too."
            );
            Assert.AreEqual(
                0,
                EditorSurfaceCaptureHostWindow.LiveHostCount,
                "The failure path must close its host window."
            );
            AssertNoLeakedCaptureTextures();
        }

        [Test]
        public void CaptureDestroysEveryObjectItCreates()
        {
            SkipWithoutGraphicsDevice();

            CaptureLabelSurface("Cleanup", nameof(CaptureDestroysEveryObjectItCreates));

            Assert.AreEqual(
                0,
                EditorSurfaceCaptureHostWindow.LiveHostCount,
                "A host window that survives a capture is a leaked native panel."
            );
            AssertNoLeakedCaptureTextures();
        }

        [Test]
        public void CaptureRefusesAnEmptySurface()
        {
            SkipWithoutGraphicsDevice();

            Assert.Throws<InvalidOperationException>(
                () =>
                    EditorSurfaceCapture.Capture(
                        new VisualElement(),
                        CanvasWidth,
                        CanvasHeight,
                        OutputPath(nameof(CaptureRefusesAnEmptySurface))
                    ),
                "A surface that lays out to nothing must be refused rather than written as an "
                    + "empty image."
            );
            Assert.AreEqual(0, EditorSurfaceCaptureHostWindow.LiveHostCount);
            AssertNoLeakedCaptureTextures();
        }

        [Test]
        public void CaptureRejectsNullContent()
        {
            Assert.Throws<ArgumentNullException>(() =>
                EditorSurfaceCapture.Capture(
                    null,
                    CanvasWidth,
                    CanvasHeight,
                    OutputPath(nameof(CaptureRejectsNullContent))
                )
            );
        }

        [Test]
        [TestCase(0, 100, TestName = "Canvas.ZeroWidth")]
        [TestCase(100, 0, TestName = "Canvas.ZeroHeight")]
        [TestCase(-10, 100, TestName = "Canvas.NegativeWidth")]
        [TestCase(100, -10, TestName = "Canvas.NegativeHeight")]
        [TestCase(int.MinValue, int.MinValue, TestName = "Canvas.MinValue")]
        public void CaptureRejectsNonPositiveCanvas(int width, int height)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                EditorSurfaceCapture.Capture(
                    BuildSolidSurface(Color.red),
                    width,
                    height,
                    OutputPath(nameof(CaptureRejectsNonPositiveCanvas))
                )
            );
        }

        [Test]
        [TestCase(null, TestName = "OutputPath.Null")]
        [TestCase("", TestName = "OutputPath.Empty")]
        [TestCase("   ", TestName = "OutputPath.Whitespace")]
        public void CaptureRejectsMissingOutputPath(string outputPath)
        {
            Assert.Throws<ArgumentException>(() =>
                EditorSurfaceCapture.Capture(
                    BuildSolidSurface(Color.red),
                    CanvasWidth,
                    CanvasHeight,
                    outputPath
                )
            );
        }

        [Test]
        public void InvokeInheritedPanelMethodReportsAMissingUnityMember()
        {
            SkipWithoutGraphicsDevice();

            EditorSurfaceCaptureHostWindow host = EditorSurfaceCaptureHostWindow.Create(
                CanvasWidth,
                CanvasHeight
            );
            try
            {
                IPanel panel = host.rootVisualElement.panel;
                Assert.IsTrue(panel != null, "A shown host window must have a panel.");

                InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
                    EditorSurfaceCapture.InvokeInheritedPanelMethod(
                        panel,
                        "ThisPanelMethodDoesNotExist",
                        Array.Empty<object>()
                    )
                );
                Assert.IsTrue(
                    0
                        <= failure.Message.IndexOf(
                            "ThisPanelMethodDoesNotExist",
                            StringComparison.Ordinal
                        ),
                    "A missing Unity panel member must be named in the diagnostic."
                );
            }
            finally
            {
                EditorSurfaceCaptureHostWindow.CloseHost(host);
            }
        }

        private static void SkipWithoutGraphicsDevice()
        {
            if (!EditorSurfaceCapture.IsSupported)
            {
                Assert.Ignore(EditorSurfaceCapture.UnsupportedReason);
            }
        }

        private static void AssertNoLeakedCaptureTextures()
        {
            List<string> leaked = new();
            Texture[] textures = Resources.FindObjectsOfTypeAll<Texture>();
            for (int index = 0; index < textures.Length; index++)
            {
                Texture texture = textures[index];
                if (texture == null)
                {
                    continue;
                }

                if (
                    string.Equals(
                        texture.name,
                        EditorSurfaceCapture.CanvasObjectName,
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        texture.name,
                        EditorSurfaceCapture.ReadbackObjectName,
                        StringComparison.Ordinal
                    )
                )
                {
                    leaked.Add(texture.name);
                }
            }

            Assert.IsEmpty(
                leaked,
                "Capture must destroy the offscreen canvas and readback texture it created; "
                    + $"found {leaked.Count} still alive."
            );
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

        private static VisualElement BuildSolidSurface(Color color)
        {
            return BuildSolidSurface(color, SurfaceWidth, SurfaceHeight);
        }

        private static VisualElement BuildSolidSurface(Color color, float width, float height)
        {
            VisualElement surface = new();
            surface.style.width = width;
            surface.style.height = height;
            surface.style.backgroundColor = color;
            return surface;
        }

        private static VisualElement BuildLabelSurface(string text)
        {
            VisualElement surface = new();
            surface.style.width = SurfaceWidth;
            surface.style.height = SurfaceHeight;
            surface.style.backgroundColor = new Color(0.2196f, 0.2196f, 0.2196f, 1f);
            surface.style.paddingLeft = 6f;
            surface.style.paddingTop = 6f;

            Label label = new(text);
            label.style.color = Color.white;
            surface.Add(label);
            return surface;
        }

        private Texture2D DecodePng(string path)
        {
            Texture2D decoded = Track(new Texture2D(2, 2, TextureFormat.RGB24, false, true));
            Assert.IsTrue(
                decoded.LoadImage(File.ReadAllBytes(path), false),
                $"The capture at {path} could not be decoded as an image."
            );
            return decoded;
        }

        private string OutputPath(string fileName)
        {
            return Path.Combine(_outputDirectory, fileName + ".png");
        }

        private EditorSurfaceCaptureResult Capture(VisualElement surface, string fileName)
        {
            return EditorSurfaceCapture.Capture(
                surface,
                CanvasWidth,
                CanvasHeight,
                OutputPath(fileName)
            );
        }

        private EditorSurfaceCaptureResult CaptureLabelSurface(string text, string fileName)
        {
            return Capture(BuildLabelSurface(text), fileName);
        }

        private EditorSurfaceCaptureResult CaptureSolidSurfaceAtOffset(
            float marginLeft,
            float marginTop,
            int canvasWidth,
            int canvasHeight,
            string fileName
        )
        {
            VisualElement surface = BuildSolidSurface(Color.red);
            surface.style.marginLeft = marginLeft;
            surface.style.marginTop = marginTop;

            EditorSurfaceCaptureResult result = EditorSurfaceCapture.Capture(
                surface,
                canvasWidth,
                canvasHeight,
                OutputPath(fileName)
            );
            Assert.AreEqual(SurfaceWidth, result.Width);
            Assert.AreEqual(SurfaceHeight, result.Height);
            return result;
        }
    }
#endif
}
