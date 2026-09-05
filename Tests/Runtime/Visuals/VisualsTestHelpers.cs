// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Visuals
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Visuals.UIToolkit;
    using Object = UnityEngine.Object;

    internal static class VisualsTestHelpers
    {
        public static Sprite CreateSprite(
            List<Object> tracked,
            int width,
            int height,
            Func<int, int, Color> pixelFactory,
            float pixelsPerUnit = 1f,
            Vector2? pivot = null
        )
        {
            Texture2D texture = new(
                width,
                height,
                TextureFormat.RGBA32,
                mipChain: false,
                linear: false
            )
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            Color[] pixels = new Color[width * height];
            for (int y = 0; y < height; ++y)
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; ++x)
                {
                    pixels[rowOffset + x] = pixelFactory(x, y);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            Vector2 pivotValue = pivot ?? new Vector2(0.5f, 0.5f);
            /*
                Unity rejects non-positive sprite PPU; return null to exercise downstream handling of that
                conceptual test input.
            */
            Sprite sprite = null;
            if (0f < pixelsPerUnit)
            {
                sprite = Sprite.Create(
                    texture,
                    new Rect(0f, 0f, width, height),
                    pivotValue,
                    pixelsPerUnit
                );
            }

            tracked.Add(texture);
            if (sprite != null)
            {
                tracked.Add(sprite);
            }
            return sprite;
        }

        public static Texture2D[] GetComputedTextures(LayeredImage image, List<Object> tracked)
        {
            Texture2D[] computed = image?.ComputedTexturesForTests;

            foreach (Texture2D texture in computed)
            {
                if (texture != null)
                {
                    tracked.Add(texture);
                }
            }

            return computed;
        }

        public static Color32[] GetPixelData(Texture2D texture)
        {
            if (texture == null)
            {
                return Array.Empty<Color32>();
            }

            Color32[] pixels = texture.GetPixels32();
            if (pixels == null || pixels.Length == 0)
            {
                return Array.Empty<Color32>();
            }

            return pixels;
        }

        public static Color32 GetPixel(Texture2D texture, int x, int y)
        {
            Color32[] pixels = GetPixelData(texture);
            if (pixels.Length == 0)
            {
                return default;
            }

            int width = texture.width;
            return pixels[y * width + x];
        }

        public static void DestroyTracked(List<Object> tracked)
        {
            for (int i = tracked.Count - 1; 0 <= i; --i)
            {
                Object instance = tracked[i];
                if (instance != null)
                {
                    Object.DestroyImmediate(instance);
                }
            }

            tracked.Clear();
        }
    }
}
