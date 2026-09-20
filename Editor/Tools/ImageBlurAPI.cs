// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools
{
#if UNITY_EDITOR
    using System;
    using UnityEngine;

    /// <summary>
    /// Creates Gaussian-blurred textures without opening the Image Blur window.
    /// </summary>
    public static class ImageBlurAPI
    {
        /// <summary>
        /// Creates a blurred copy of a readable texture. The caller owns the returned texture and
        /// must destroy it when finished.
        /// </summary>
        public static bool TryBlur(
            Texture2D source,
            int radius,
            out Texture2D blurred,
            out string error
        )
        {
            blurred = null;
            error = null;
            if (source == null)
            {
                error = "A source texture is required.";
                return false;
            }

            if (radius < 1 || 200 < radius)
            {
                error = "Blur radius must be between 1 and 200.";
                return false;
            }

            if (!source.isReadable)
            {
                error = "The source texture must be readable.";
                return false;
            }

            try
            {
                blurred = ImageBlurTool.CreateBlurredTexture(source, radius, null);
                if (blurred != null)
                {
                    return true;
                }

                error = $"Could not blur texture '{source.name}'.";
                return false;
            }
            catch (Exception exception)
            {
                error = $"Could not blur texture '{source.name}': {exception.Message}";
                return false;
            }
        }
    }
#endif
}
