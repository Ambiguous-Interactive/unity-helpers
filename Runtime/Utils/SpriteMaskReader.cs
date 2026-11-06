// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
#if UNITY_EDITOR
    using System.IO;
    using UnityEditor;
    using PackageInfo = UnityEditor.PackageManager.PackageInfo;
#endif

    /// <summary>Reads sprite alpha without changing texture import settings.</summary>
    public static class SpriteMaskReader
    {
        /// <summary>Reads source PNG pixels in the Editor, or readable unpacked texture pixels in a player.</summary>
        /// <param name="sprite">The sprite to read on Unity's main thread.</param>
        /// <param name="mask">New mask pixels and their local coordinate mapping, or default on failure.</param>
        /// <param name="error">The reason reading failed, or null on success.</param>
        /// <param name="alphaThreshold">The minimum nonzero alpha byte considered opaque.</param>
        /// <returns>Whether the sprite's exact pixel mask could be read.</returns>
        /// <remarks>
        /// Alpha zero is always transparent; other pixels are opaque at or above the threshold.
        /// Editor PNG masks honor the texture importer's alpha source.
        /// Separate alpha textures require original PNG pixels in the Editor.
        /// </remarks>
        public static bool TryRead(
            Sprite sprite,
            out SpriteAlphaMask mask,
            out string error,
            byte alphaThreshold = 1
        )
        {
            try
            {
                if (!TryReadPixels(sprite, out SpritePixelRegion region, out string failure))
                {
                    error = failure;
                    mask = default;
                    return false;
                }
                int width = region.Region.width;
                int height = region.Region.height;
                int count = width * height;
                bool[] opaque = new bool[count];
                for (int y = 0; y < height; ++y)
                {
                    int offset = y * width;
                    for (int x = 0; x < width; ++x)
                    {
                        byte alpha = region.GetPixel(x, y).a;
                        opaque[offset + x] = alpha != 0 && alphaThreshold <= alpha;
                    }
                }
                mask = new SpriteAlphaMask(
                    opaque,
                    width,
                    height,
                    region.Pivot,
                    region.UnitsPerPixel
                );
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                mask = default;
                return false;
            }
        }

        internal static bool TryReadPixels(
            Sprite sprite,
            out SpritePixelRegion region,
            out string error
        )
        {
            if (sprite == null)
            {
                error = "A live sprite is required.";
                region = default;
                return false;
            }
            try
            {
                float pixelsPerUnit = sprite.pixelsPerUnit;
                Vector2 pivot = sprite.pivot;
                if (
                    !(0 < pixelsPerUnit)
                    || !WallMath.IsFinite(pixelsPerUnit)
                    || !WallMath.IsFinite(pivot)
                )
                {
                    error = "The sprite needs a finite pivot and positive finite pixels per unit.";
                    region = default;
                    return false;
                }
#if UNITY_EDITOR
                string assetPath = AssetDatabase.GetAssetPath(sprite);
                if (
                    !string.IsNullOrWhiteSpace(assetPath)
                    && string.Equals(
                        Path.GetExtension(assetPath),
                        ".png",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    string physicalPath;
                    if (assetPath.StartsWith("Packages/", StringComparison.Ordinal))
                    {
                        PackageInfo package = PackageInfo.FindForAssetPath(assetPath);
                        if (package == null)
                        {
                            error = "The sprite's package source could not be located.";
                            region = default;
                            return false;
                        }
                        physicalPath = Path.Combine(
                            package.resolvedPath,
                            assetPath.Substring(package.assetPath.Length + 1)
                        );
                    }
                    else
                    {
                        string projectRoot = Path.GetDirectoryName(Application.dataPath);
                        physicalPath = Path.Combine(projectRoot, assetPath);
                    }
                    Texture2D imported = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                    TextureImporter importer =
                        AssetImporter.GetAtPath(assetPath) as TextureImporter;
                    if (imported == null || importer == null)
                    {
                        error = "The sprite's source texture could not be located.";
                        region = default;
                        return false;
                    }
                    using ScratchTexture scratch = new(true);
                    byte[] encoded = File.ReadAllBytes(physicalPath);
                    if (!ImageConversion.LoadImage(scratch.Texture, encoded, false))
                    {
                        error = "The sprite's source PNG could not be decoded.";
                        region = default;
                        return false;
                    }
                    return TryReadPixels(
                        sprite,
                        scratch.Texture,
                        imported.width,
                        imported.height,
                        out region,
                        out error,
                        importer.alphaSource == TextureImporterAlphaSource.None,
                        importer.alphaSource == TextureImporterAlphaSource.FromGrayScale
                    );
                }
#endif
                if (sprite.associatedAlphaSplitTexture != null)
                {
                    error =
                        "Sprites with a separate alpha texture require source PNG pixels or an explicit alpha mask.";
                    region = default;
                    return false;
                }
                Texture2D texture = sprite.texture;
                if (texture == null || !texture.isReadable || sprite.packed)
                {
                    error =
                        "Exact sprite art needs a source PNG in the Editor or a readable, unpacked texture at runtime. Bake the collider in the Editor and disable this component for unreadable player textures.";
                    region = default;
                    return false;
                }
                return TryReadPixels(
                    sprite,
                    texture,
                    texture.width,
                    texture.height,
                    out region,
                    out error
                );
            }
            catch (Exception exception)
            {
                error = exception.Message;
                region = default;
                return false;
            }
        }

        private static bool TryReadPixels(
            Sprite sprite,
            Texture2D texture,
            int importedWidth,
            int importedHeight,
            out SpritePixelRegion region,
            out string error,
            bool ignoreSourceAlpha = false,
            bool alphaFromGrayscale = false
        )
        {
            Rect rectangle = sprite.rect;
            double scaleX = (double)texture.width / importedWidth;
            double scaleY = (double)texture.height / importedHeight;
            double left = rectangle.xMin * scaleX;
            double bottom = rectangle.yMin * scaleY;
            double right = rectangle.xMax * scaleX;
            double top = rectangle.yMax * scaleY;
            if (
                0.001 < Math.Abs(left - Math.Round(left))
                || 0.001 < Math.Abs(bottom - Math.Round(bottom))
                || 0.001 < Math.Abs(right - Math.Round(right))
                || 0.001 < Math.Abs(top - Math.Round(top))
            )
            {
                error =
                    "The sprite rectangle does not align with source pixel corners; use an unscaled texture import for exact tracing.";
                region = default;
                return false;
            }
            int xStart = (int)Math.Round(left);
            int yStart = (int)Math.Round(bottom);
            int width = (int)Math.Round(right) - xStart;
            int height = (int)Math.Round(top) - yStart;
            if (
                xStart < 0
                || yStart < 0
                || width <= 0
                || height <= 0
                || texture.width < (long)xStart + width
                || texture.height < (long)yStart + height
                || int.MaxValue < (long)width * height
            )
            {
                error = "The sprite rectangle is outside its source texture.";
                region = default;
                return false;
            }
            Vector2 maskPivot = new(
                (float)(sprite.pivot.x * scaleX),
                (float)(sprite.pivot.y * scaleY)
            );
            Vector2 unitsPerPixel = new(
                (float)(1 / (sprite.pixelsPerUnit * scaleX)),
                (float)(1 / (sprite.pixelsPerUnit * scaleY))
            );
            if (
                !WallMath.IsFinite(maskPivot)
                || !(0 < unitsPerPixel.x)
                || !WallMath.IsFinite(unitsPerPixel.x)
                || !(0 < unitsPerPixel.y)
                || !WallMath.IsFinite(unitsPerPixel.y)
            )
            {
                error = "The sprite's source coordinates cannot be represented in local units.";
                region = default;
                return false;
            }
            Color32[] pixels = texture.GetPixels32();
            if ((long)texture.width * texture.height != pixels.Length)
            {
                error = "The texture pixels do not match its dimensions.";
                region = default;
                return false;
            }
            RectInt sourceRegion = new(xStart, yStart, width, height);
            region = new SpritePixelRegion(
                pixels,
                texture.width,
                sourceRegion,
                maskPivot,
                unitsPerPixel,
                ignoreSourceAlpha,
                alphaFromGrayscale
            );
            error = null;
            return true;
        }

#if UNITY_EDITOR
        private readonly struct ScratchTexture : IDisposable
        {
            internal readonly Texture2D Texture;

            internal ScratchTexture(bool initialize = true)
            {
                Texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }

            /// <summary>Releases the temporary decoded texture.</summary>
            public void Dispose()
            {
                if (Texture == null)
                {
                    return;
                }
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(Texture);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(Texture);
                }
            }
        }
#endif
    }
}
