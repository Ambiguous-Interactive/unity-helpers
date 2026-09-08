// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Capture
{
    using System.IO;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Editor;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    public sealed class SpritePreviewRenderingTests : CommonTestBase
    {
        [TestCase(FilterMode.Bilinear, 104)]
        [TestCase(FilterMode.Bilinear, 10)]
        [TestCase(FilterMode.Trilinear, 104)]
        [TestCase(FilterMode.Trilinear, 10)]
        public void SourceCropMatchesCopiedPreviewPixelsAndRestoresSampler(
            FilterMode filter,
            int width
        )
        {
            if (!EditorSurfaceCapture.IsSupported)
            {
                Assert.Ignore(EditorSurfaceCapture.UnsupportedReason);
            }
            Texture2D source = Track(new Texture2D(16, 8));
            for (int y = 0; y < source.height; y++)
            {
                for (int x = 0; x < source.width; x++)
                {
                    source.SetPixel(x, y, (x + y) % 2 == 0 ? Color.red : Color.green);
                }
            }
            source.Apply();
            source.filterMode = filter;
            source.wrapModeU = TextureWrapMode.Repeat;
            source.wrapModeV = TextureWrapMode.Mirror;
            source.wrapModeW = TextureWrapMode.Clamp;
            Rect crop = new(2.4f, 1.5f, 7.6f, 3.2f);
            Texture2D copy = Track(AnimationEventSpritePreviewRenderer.CopyTexture(crop, source));
            string directory = Path.Combine(
                Path.GetTempPath(),
                nameof(SpritePreviewRenderingTests)
            );
            Directory.CreateDirectory(directory);
            string originalPath = Path.Combine(directory, "original.png");
            string croppedPath = Path.Combine(directory, "cropped.png");
            try
            {
                IMGUIContainer original = new(() => GUILayout.Label(copy));
                original.style.width = width;
                original.style.height = 32;
                IMGUIContainer cropped = new(() =>
                    AnimationEventSpritePreviewRenderer.DrawSourceCrop(source, crop)
                );
                cropped.style.width = width;
                cropped.style.height = 32;
                EditorSurfaceCapture.Capture(original, 128, 64, originalPath);
                EditorSurfaceCapture.Capture(cropped, 128, 64, croppedPath);
                Texture2D originalImage = Track(new Texture2D(2, 2));
                Texture2D croppedImage = Track(new Texture2D(2, 2));
                Assert.IsTrue(originalImage.LoadImage(File.ReadAllBytes(originalPath)));
                Assert.IsTrue(croppedImage.LoadImage(File.ReadAllBytes(croppedPath)));
                Color32[] originalPixels = originalImage.GetPixels32();
                Assert.IsTrue(
                    System.Array.Exists(originalPixels, pixel => 200 < pixel.r && pixel.g < 50),
                    "The reference must render the red control pixels."
                );
                CollectionAssert.AreEqual(originalPixels, croppedImage.GetPixels32());
                Assert.AreEqual(filter, source.filterMode);
                Assert.AreEqual(TextureWrapMode.Repeat, source.wrapModeU);
                Assert.AreEqual(TextureWrapMode.Mirror, source.wrapModeV);
                Assert.AreEqual(TextureWrapMode.Clamp, source.wrapModeW);
            }
            finally
            {
                File.Delete(originalPath);
                File.Delete(croppedPath);
            }
        }
    }
}
