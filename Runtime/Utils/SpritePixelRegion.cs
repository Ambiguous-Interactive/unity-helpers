// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;

    internal readonly struct SpritePixelRegion
    {
        internal Color32[] Pixels { get; }
        internal int TextureWidth { get; }
        internal RectInt Region { get; }
        internal Vector2 Pivot { get; }
        internal Vector2 UnitsPerPixel { get; }
        internal bool IgnoreSourceAlpha { get; }
        internal bool AlphaFromGrayscale { get; }

        internal SpritePixelRegion(
            Color32[] pixels,
            int textureWidth,
            RectInt region,
            Vector2 pivot,
            Vector2 unitsPerPixel,
            bool ignoreSourceAlpha = false,
            bool alphaFromGrayscale = false
        )
        {
            Pixels = pixels;
            TextureWidth = textureWidth;
            Region = region;
            Pivot = pivot;
            UnitsPerPixel = unitsPerPixel;
            IgnoreSourceAlpha = ignoreSourceAlpha;
            AlphaFromGrayscale = alphaFromGrayscale;
        }

        internal bool TryGetPixelCount(out int count)
        {
            int width = Region.width;
            int height = Region.height;
            if (
                Pixels == null
                || TextureWidth <= 0
                || width <= 0
                || height <= 0
                || Region.x < 0
                || Region.y < 0
                || Pixels.Length % TextureWidth != 0
                || TextureWidth < (long)Region.x + width
                || Pixels.Length / TextureWidth < (long)Region.y + height
                || !WallMath.IsFinite(Pivot)
                || !WallMath.IsFinite(UnitsPerPixel)
                || UnitsPerPixel.x <= 0
                || UnitsPerPixel.y <= 0
            )
            {
                count = 0;
                return false;
            }
            count = width * height;
            return true;
        }

        internal Color32 GetPixel(int index)
        {
            int width = Region.width;
            int x = index % width;
            int y = index / width;
            return GetPixel(x, y);
        }

        internal Color32 GetPixel(int x, int y)
        {
            int pixelIndex = (Region.y + y) * TextureWidth + Region.x + x;
            Color32 pixel = Pixels[pixelIndex];
            if (IgnoreSourceAlpha)
            {
                pixel.a = byte.MaxValue;
            }
            else if (AlphaFromGrayscale)
            {
                pixel.a = (byte)((pixel.r + pixel.g + pixel.b) / 3);
            }
            return pixel;
        }
    }
}
