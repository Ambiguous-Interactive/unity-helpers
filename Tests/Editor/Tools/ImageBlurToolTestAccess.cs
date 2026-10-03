// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.CustomEditors;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Utils;
    using static WallstopStudios.UnityHelpers.Editor.Tools.ImageBlurTool;
    using Object = UnityEngine.Object;

    /// <summary>Provides test-side setup and inspection for ImageBlurTool.</summary>
    internal static class ImageBlurToolTestAccess
    {
        internal static Texture2D Blurred(Texture2D original, int radius)
        {
            return CreateBlurredTexture(original, radius);
        }

        internal static Texture2D Blurred(Texture2D original, int radius, bool runInParallel)
        {
            Color[] pixels = original.GetPixels();
            Color[] premultiplied = new Color[pixels.Length];
            for (int i = 0; i < pixels.Length; ++i)
            {
                premultiplied[i] = TextureResampling.Premultiply(pixels[i]);
            }
            Color[] output = new Color[pixels.Length];
            BlurJob job = new(
                original.width,
                original.height,
                radius,
                GenerateGaussianKernel(radius),
                pixels,
                premultiplied,
                new Color[pixels.Length],
                new Color[pixels.Length],
                output
            );
            job.ExecuteHorizontalPass(runInParallel);
            job.ExecuteVerticalPass(runInParallel);
            Texture2D blurred = new(original.width, original.height, original.format, false);
            try
            {
                blurred.SetPixels(output);
                blurred.Apply();
                return blurred;
            }
            catch
            {
                Object.DestroyImmediate(blurred); // UNH-SUPPRESS: Failed construction has not transferred the texture to caller tracking.
                throw;
            }
        }
    }
#endif
}
