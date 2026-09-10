// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using Extension;
    using UnityEngine;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /// <summary>
    /// Sprite and texture utilities for editor workflows.
    /// </summary>
    public static class SpriteHelpers
    {
        /// <summary>
        /// Ensures a Texture2D asset is marked as readable (Editor only). No-ops in player.
        /// </summary>
        /// <remarks>
        /// Useful for analysis or runtime generation workflows that require raw texture data.
        /// </remarks>
        public static void MakeReadable(this Texture2D texture)
        {
            if (texture == null || texture.isReadable)
            {
                return;
            }

#if UNITY_EDITOR
            string assetPath = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(assetPath))
            {
                texture.LogError($"Failed to get asset path.");
                return;
            }

            TextureImporter tImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (tImporter == null)
            {
                texture.LogError($"Failed to get texture importer.");
                return;
            }

            if (!tImporter.isReadable)
            {
                tImporter.isReadable = true;
                EditorUtility.SetDirty(tImporter);
                tImporter.SaveAndReimport();
                EditorUtility.SetDirty(texture);
            }
#endif
        }

        /// <summary>
        /// Returns a new Texture2D rotated 90 degrees. The source texture is not modified.
        /// </summary>
        /// <param name="texture">The texture to rotate.</param>
        /// <param name="clockwise">When true, rotates clockwise; otherwise counterclockwise.</param>
        /// <returns>
        /// The rotated texture with the source's format, or null when <paramref name="texture"/>
        /// is null, not readable, or its format does not support pixel writes. The caller owns the
        /// returned texture and must destroy it when finished with it.
        /// </returns>
        public static Texture2D RotateTexture90(this Texture2D texture, bool clockwise)
        {
            if (texture == null)
            {
                return null;
            }

            int sourceWidth = texture.width;
            int sourceHeight = texture.height;
            if (!texture.isReadable || sourceWidth < 1 || sourceHeight < 1)
            {
                texture.LogError($"RotateTexture90 requires a readable texture with pixels.");
                return null;
            }

            Color32[] source = texture.GetPixels32();
            int rotatedWidth = sourceHeight;
            int rotatedHeight = sourceWidth;
            Color32[] rotated = new Color32[source.Length];
            for (int y = 0; y < rotatedHeight; ++y)
            {
                int rowStart = y * rotatedWidth;
                for (int x = 0; x < rotatedWidth; ++x)
                {
                    rotated[rowStart + x] = clockwise
                        ? source[x * sourceWidth + (sourceWidth - 1 - y)]
                        : source[(sourceHeight - 1 - x) * sourceWidth + y];
                }
            }

            return CreateTexture(texture, rotatedWidth, rotatedHeight, rotated, "RotateTexture90");
        }

        /// <summary>
        /// Returns a new Texture2D rotated 180 degrees. The source texture is not modified.
        /// </summary>
        /// <param name="texture">The texture to rotate.</param>
        /// <returns>
        /// The rotated texture with the source's format, or null when <paramref name="texture"/>
        /// is null, not readable, or its format does not support pixel writes. The caller owns the
        /// returned texture and must destroy it when finished with it.
        /// </returns>
        public static Texture2D RotateTexture180(this Texture2D texture)
        {
            if (texture == null)
            {
                return null;
            }

            int sourceWidth = texture.width;
            int sourceHeight = texture.height;
            if (!texture.isReadable || sourceWidth < 1 || sourceHeight < 1)
            {
                texture.LogError($"RotateTexture180 requires a readable texture with pixels.");
                return null;
            }

            Color32[] source = texture.GetPixels32();
            Color32[] rotated = new Color32[source.Length];
            int last = source.Length - 1;
            for (int index = 0; index <= last; ++index)
            {
                rotated[index] = source[last - index];
            }

            return CreateTexture(texture, sourceWidth, sourceHeight, rotated, "RotateTexture180");
        }

        /// <summary>
        /// Returns a new Texture2D containing the pixels of <paramref name="sprite"/>'s region in
        /// its source texture. A fractional rect expands to the whole pixels it touches.
        /// </summary>
        /// <param name="sprite">The sprite to extract.</param>
        /// <returns>
        /// The extracted texture with the source's format, or null when <paramref name="sprite"/>
        /// is null, its texture is null or not readable, its rect lies outside the texture, or its
        /// format does not support pixel writes. The caller owns the returned texture and must
        /// destroy it when finished with it.
        /// </returns>
        public static Texture2D ExtractSpriteRect(this Sprite sprite)
        {
            if (sprite == null)
            {
                return null;
            }

            Texture2D texture = sprite.texture;
            if (texture == null)
            {
                return null;
            }

            if (!texture.isReadable)
            {
                texture.LogError($"ExtractSpriteRect requires a readable texture.");
                return null;
            }

            Rect rect;
            try
            {
                rect = sprite.textureRect;
            }
            catch (Exception exception)
            {
                sprite.LogError($"Failed to read the sprite's textureRect: {exception}");
                return null;
            }

            int startX = Mathf.FloorToInt(rect.x);
            int startY = Mathf.FloorToInt(rect.y);
            int endX = Mathf.CeilToInt(rect.xMax);
            int endY = Mathf.CeilToInt(rect.yMax);
            if (
                endX < startX
                || endY < startY
                || startX < 0
                || startY < 0
                || texture.width < endX
                || texture.height < endY
            )
            {
                sprite.LogError($"ExtractSpriteRect requires a sprite rect inside its texture.");
                return null;
            }

            int width = endX - startX;
            int height = endY - startY;
            Color32[] allPixels = texture.GetPixels32();
            Color32[] region = new Color32[width * height];
            int textureWidth = texture.width;
            int index = 0;
            for (int y = startY; y < endY; ++y)
            {
                int rowStart = y * textureWidth + startX;
                for (int x = 0; x < width; ++x)
                {
                    region[index] = allPixels[rowStart + x];
                    ++index;
                }
            }
            return CreateTexture(texture, width, height, region, "ExtractSpriteRect");
        }

        private static Texture2D CreateTexture(
            Texture2D source,
            int width,
            int height,
            Color32[] pixels,
            string operation
        )
        {
            Texture2D result = null;
            try
            {
                result = new Texture2D(width, height, source.format, 1 < source.mipmapCount);
                result.SetPixels32(pixels);
                result.Apply();
                return result;
            }
            catch (Exception exception)
            {
                if (result != null)
                {
                    UnityEngine.Object.DestroyImmediate(result);
                }
                source.LogError($"{operation} failed to write pixels: {exception}");
                return null;
            }
        }
    }
}
