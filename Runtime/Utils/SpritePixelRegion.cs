// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using Unity.Collections;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>Holds owned pixels or a texture view used only during its synchronous read.</summary>
    internal readonly struct SpritePixelRegion
    {
        internal Color32[] Pixels { get; }
        internal int TextureWidth { get; }
        internal RectInt Region { get; }
        internal Vector2 Pivot { get; }
        internal Vector2 UnitsPerPixel { get; }
        internal bool IgnoreSourceAlpha { get; }
        internal bool AlphaFromGrayscale { get; }

        private readonly NativeArray<byte> _rawPixels;
        private readonly TextureFormat _format;
        private readonly int _bytesPerPixel;

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
            _rawPixels = default;
            _format = default;
            _bytesPerPixel = 0;
        }

        internal SpritePixelRegion(
            NativeArray<byte> pixels,
            TextureFormat format,
            int bytesPerPixel,
            int textureWidth,
            RectInt region,
            Vector2 pivot,
            Vector2 unitsPerPixel,
            bool ignoreSourceAlpha = false,
            bool alphaFromGrayscale = false
        )
        {
            Pixels = null;
            TextureWidth = textureWidth;
            Region = region;
            Pivot = pivot;
            UnitsPerPixel = unitsPerPixel;
            IgnoreSourceAlpha = ignoreSourceAlpha;
            AlphaFromGrayscale = alphaFromGrayscale;
            _rawPixels = pixels;
            _format = format;
            _bytesPerPixel = bytesPerPixel;
        }

        internal static bool TryGetBytesPerPixel(TextureFormat format, out int bytesPerPixel)
        {
            switch (format)
            {
                case TextureFormat.RGBA32:
                case TextureFormat.ARGB32:
                case TextureFormat.BGRA32:
                    bytesPerPixel = 4;
                    return true;
                case TextureFormat.RGB24:
                    bytesPerPixel = 3;
                    return true;
                case TextureFormat.RGB565:
                case TextureFormat.RGBA4444:
                    bytesPerPixel = 2;
                    return true;
                case TextureFormat.Alpha8:
                    bytesPerPixel = 1;
                    return true;
                default:
                    bytesPerPixel = default;
                    return false;
            }
        }

        internal bool TryGetPixelCount(out int count)
        {
            int capacity =
                Pixels != null ? Pixels.Length
                : _rawPixels.IsCreated
                && 0 < _bytesPerPixel
                && _rawPixels.Length % _bytesPerPixel == 0
                    ? _rawPixels.Length / _bytesPerPixel
                : 0;
            int width = Region.width;
            int height = Region.height;
            if (
                TextureWidth <= 0
                || width <= 0
                || height <= 0
                || Region.x < 0
                || Region.y < 0
                || capacity % TextureWidth != 0
                || TextureWidth < (long)Region.x + width
                || capacity / TextureWidth < (long)Region.y + height
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

        internal byte GetAlpha(int x, int y)
        {
            if (Pixels != null || IgnoreSourceAlpha || AlphaFromGrayscale)
            {
                return GetPixel(x, y).a;
            }
            int index = (Region.y + y) * TextureWidth + Region.x + x;
            int offset = index * _bytesPerPixel;
            switch (_format)
            {
                case TextureFormat.RGBA32:
                case TextureFormat.BGRA32:
                    return _rawPixels[offset + 3];
                case TextureFormat.ARGB32:
                case TextureFormat.Alpha8:
                    return _rawPixels[offset];
                case TextureFormat.RGB24:
                case TextureFormat.RGB565:
                    return byte.MaxValue;
                case TextureFormat.RGBA4444:
                    return (byte)((_rawPixels.Reinterpret<ushort>(1)[index] & 15) * 17);
                default:
                    return 0;
            }
        }

        internal Color32 GetPixel(int index)
        {
            int width = Region.width;
            return GetPixel(index % width, index / width);
        }

        internal Color32 GetPixel(int x, int y)
        {
            int pixelIndex = (Region.y + y) * TextureWidth + Region.x + x;
            Color32 pixel = Pixels != null ? Pixels[pixelIndex] : ReadTexturePixel(pixelIndex);
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

        private Color32 ReadTexturePixel(int index)
        {
            int offset = index * _bytesPerPixel;
            switch (_format)
            {
                case TextureFormat.RGBA32:
                    return new Color32(
                        _rawPixels[offset],
                        _rawPixels[offset + 1],
                        _rawPixels[offset + 2],
                        _rawPixels[offset + 3]
                    );
                case TextureFormat.ARGB32:
                    return new Color32(
                        _rawPixels[offset + 1],
                        _rawPixels[offset + 2],
                        _rawPixels[offset + 3],
                        _rawPixels[offset]
                    );
                case TextureFormat.BGRA32:
                    return new Color32(
                        _rawPixels[offset + 2],
                        _rawPixels[offset + 1],
                        _rawPixels[offset],
                        _rawPixels[offset + 3]
                    );
                case TextureFormat.RGB24:
                    return new Color32(
                        _rawPixels[offset],
                        _rawPixels[offset + 1],
                        _rawPixels[offset + 2],
                        byte.MaxValue
                    );
                case TextureFormat.Alpha8:
                    return new Color32(0, 0, 0, _rawPixels[offset]);
                case TextureFormat.RGB565:
                    ushort rgb = _rawPixels.Reinterpret<ushort>(1)[index];
                    int red = rgb >> 11;
                    int green = (rgb >> 5) & 63;
                    int blue = rgb & 31;
                    return new Color32(
                        (byte)((red << 3) | (red >> 2)),
                        (byte)((green << 2) | (green >> 4)),
                        (byte)((blue << 3) | (blue >> 2)),
                        byte.MaxValue
                    );
                case TextureFormat.RGBA4444:
                    ushort rgba = _rawPixels.Reinterpret<ushort>(1)[index];
                    return new Color32(
                        (byte)((rgba >> 12) * 17),
                        (byte)(((rgba >> 8) & 15) * 17),
                        (byte)(((rgba >> 4) & 15) * 17),
                        (byte)((rgba & 15) * 17)
                    );
                default:
                    return default;
            }
        }
    }
}
