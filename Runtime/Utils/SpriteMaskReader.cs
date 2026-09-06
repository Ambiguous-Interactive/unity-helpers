// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using UnityEngine;
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
        /// <remarks>Alpha zero is always transparent; other pixels are opaque at or above the threshold.</remarks>
        public static bool TryRead(
            Sprite sprite,
            out SpriteAlphaMask mask,
            out string error,
            byte alphaThreshold = 1
        )
        {
            if (sprite == null)
            {
                error = "A live sprite is required.";
                mask = default;
                return false;
            }
            try
            {
                float pixelsPerUnit = sprite.pixelsPerUnit;
                Vector2 pivot = sprite.pivot;
                if (
                    !(0 < pixelsPerUnit)
                    || float.IsInfinity(pixelsPerUnit)
                    || float.IsNaN(pivot.x)
                    || float.IsInfinity(pivot.x)
                    || float.IsNaN(pivot.y)
                    || float.IsInfinity(pivot.y)
                )
                {
                    error = "The sprite needs a finite pivot and positive finite pixels per unit.";
                    mask = default;
                    return false;
                }
#if UNITY_EDITOR
                string assetPath = AssetDatabase.GetAssetPath(sprite);
                if (
                    !string.IsNullOrEmpty(assetPath)
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
                            mask = default;
                            return false;
                        }
                        physicalPath = Path.Combine(
                            package.resolvedPath,
                            assetPath.Substring(package.assetPath.Length + 1)
                        );
                    }
                    else
                    {
                        physicalPath = Path.Combine(
                            Path.GetDirectoryName(Application.dataPath),
                            assetPath
                        );
                    }
                    Texture2D imported = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                    if (imported == null)
                    {
                        error = "The sprite's source texture could not be located.";
                        mask = default;
                        return false;
                    }
                    using ScratchTexture scratch = new(true);
                    if (
                        !ImageConversion.LoadImage(
                            scratch.Texture,
                            File.ReadAllBytes(physicalPath),
                            false
                        )
                    )
                    {
                        error = "The sprite's source PNG could not be decoded.";
                        mask = default;
                        return false;
                    }
                    return TryReadPixels(
                        sprite,
                        scratch.Texture,
                        imported.width,
                        imported.height,
                        alphaThreshold,
                        out mask,
                        out error
                    );
                }
#endif
                Texture2D texture = sprite.texture;
                if (texture == null || !texture.isReadable || sprite.packed)
                {
                    error =
                        "Exact sprite art needs a source PNG in the Editor or a readable, unpacked texture at runtime. Bake the collider in the Editor and disable this component for unreadable player textures.";
                    mask = default;
                    return false;
                }
                return TryReadPixels(
                    sprite,
                    texture,
                    texture.width,
                    texture.height,
                    alphaThreshold,
                    out mask,
                    out error
                );
            }
            catch (Exception exception)
            {
                error = exception.Message;
                mask = default;
                return false;
            }
        }

        private static bool TryReadPixels(
            Sprite sprite,
            Texture2D texture,
            int importedWidth,
            int importedHeight,
            byte alphaThreshold,
            out SpriteAlphaMask mask,
            out string error
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
                mask = default;
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
                mask = default;
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
                float.IsNaN(maskPivot.x)
                || float.IsInfinity(maskPivot.x)
                || float.IsNaN(maskPivot.y)
                || float.IsInfinity(maskPivot.y)
                || !(0 < unitsPerPixel.x)
                || float.IsInfinity(unitsPerPixel.x)
                || !(0 < unitsPerPixel.y)
                || float.IsInfinity(unitsPerPixel.y)
            )
            {
                error = "The sprite's source coordinates cannot be represented in local units.";
                mask = default;
                return false;
            }
            Color32[] pixels = texture.GetPixels32();
            bool[] opaque = new bool[width * height];
            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    byte alpha = pixels[(yStart + y) * texture.width + xStart + x].a;
                    opaque[y * width + x] = alpha != 0 && alphaThreshold <= alpha;
                }
            }
            mask = new SpriteAlphaMask(opaque, width, height, maskPivot, unitsPerPixel);
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
