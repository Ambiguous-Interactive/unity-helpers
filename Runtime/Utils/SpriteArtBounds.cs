// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>Measures the bounds of drawn sprite pixels and their transformed corners.</summary>
    public static class SpriteArtBounds
    {
        /// <summary>Reads the local bounds of nontransparent sprite pixels without changing import settings.</summary>
        /// <remarks>
        /// Uses original PNG pixels in the Editor or readable texture pixels. Packed atlas fallbacks
        /// require rectangular, unrotated packing; tight and rotated atlas data are refused.
        /// Separate alpha textures require source PNG pixels or the mask overload.
        /// </remarks>
        public static bool TryGetLocalArtBounds(
            Sprite sprite,
            out Bounds bounds,
            out string error,
            byte alphaThreshold = 1
        )
        {
            if (
                !SpriteMaskReader.TryRead(
                    sprite,
                    out SpriteAlphaMask mask,
                    out string failure,
                    alphaThreshold
                )
            )
            {
                if (sprite != null && sprite.packed && sprite.associatedAlphaSplitTexture == null)
                {
                    return TryGetPackedArtBounds(sprite, alphaThreshold, out bounds, out error);
                }
                error = failure;
                bounds = default;
                return false;
            }

            return TryGetLocalArtBounds(mask, out bounds, out error);
        }

        /// <summary>Measures the local bounds of a previously read mask without allocating or copying pixels.</summary>
        /// <remarks>Transparent, malformed or unrepresentable masks fail with empty bounds and a reason.</remarks>
        public static bool TryGetLocalArtBounds(
            SpriteAlphaMask mask,
            out Bounds bounds,
            out string error
        )
        {
            int width = mask.Width;
            int height = mask.Height;
            Vector2 pivot = mask.Pivot;
            Vector2 units = mask.UnitsPerPixel;
            bool[] pixels = mask.Pixels;
            if (
                pixels == null
                || width <= 0
                || height <= 0
                || (long)width * height != pixels.Length
                || !WallMath.IsFinite(pivot)
                || !WallMath.IsFinite(units)
                || units.x <= 0
                || units.y <= 0
            )
            {
                error =
                    "The sprite mask needs matching dimensions and finite local coordinates with positive pixel sizes.";
                bounds = default;
                return false;
            }

            int left = width;
            int bottom = height;
            int right = -1;
            int top = -1;
            for (int row = 0; row < height; ++row)
            {
                int offset = row * width;
                for (int column = 0; column < width; ++column)
                {
                    if (!pixels[offset + column])
                    {
                        continue;
                    }

                    left = Mathf.Min(left, column);
                    bottom = Mathf.Min(bottom, row);
                    right = Mathf.Max(right, column);
                    top = row;
                }
            }

            if (right < 0)
            {
                error = "The sprite mask is fully transparent.";
                bounds = default;
                return false;
            }

            double minX = (left - (double)pivot.x) * units.x;
            double minY = (bottom - (double)pivot.y) * units.y;
            double maxX = (right + 1 - (double)pivot.x) * units.x;
            double maxY = (top + 1 - (double)pivot.y) * units.y;
            return TryCreateBounds(minX, minY, maxX, maxY, out bounds, out error);
        }

        /// <summary>Mirrors local art bounds about the renderer origin on each flipped axis.</summary>
        public static Bounds AsDrawn(Bounds localArt, bool flipX, bool flipY)
        {
            Vector3 center = localArt.center;
            if (flipX)
            {
                center.x = -center.x;
            }
            if (flipY)
            {
                center.y = -center.y;
            }
            localArt.center = center;
            return localArt;
        }

        /// <summary>Returns the lowest transformed corner of the art rectangle in its center's local Z plane.</summary>
        public static float LowestCornerY(Matrix4x4 toWorld, Bounds localArt)
        {
            Vector3 minimum = localArt.min;
            Vector3 maximum = localArt.max;
            float z = localArt.center.z;
            float lowest = float.PositiveInfinity;
            for (int corner = 0; corner < 4; ++corner)
            {
                float x = (corner & 1) == 0 ? minimum.x : maximum.x;
                float y = (corner & 2) == 0 ? minimum.y : maximum.y;
                Vector3 world = toWorld.MultiplyPoint3x4(new Vector3(x, y, z));
                lowest = Mathf.Min(lowest, world.y);
            }

            return lowest;
        }

        private static bool TryCreateBounds(
            double minX,
            double minY,
            double maxX,
            double maxY,
            out Bounds bounds,
            out string error
        )
        {
            Vector3 center = new((float)((minX + maxX) * 0.5), (float)((minY + maxY) * 0.5), 0f);
            Vector3 size = new((float)(maxX - minX), (float)(maxY - minY), 0f);
            Bounds result = new(center, size);
            Vector3 minimum = result.min;
            Vector3 maximum = result.max;
            if (
                !WallMath.IsFinite(center)
                || !WallMath.IsFinite(size)
                || !WallMath.IsFinite(minimum)
                || !WallMath.IsFinite(maximum)
                || maximum.x <= minimum.x
                || maximum.y <= minimum.y
            )
            {
                error = "The sprite art bounds cannot be represented in local coordinates.";
                bounds = default;
                return false;
            }

            error = null;
            bounds = result;
            return true;
        }

        private static bool TryGetPackedArtBounds(
            Sprite sprite,
            byte alphaThreshold,
            out Bounds bounds,
            out string error
        )
        {
            try
            {
                Texture2D texture = sprite.texture;
                if (
                    texture == null
                    || !texture.isReadable
                    || sprite.packingMode != SpritePackingMode.Rectangle
                    || sprite.packingRotation != SpritePackingRotation.None
                )
                {
                    error = "Packed sprite art requires a readable, rectangular, unrotated atlas.";
                    bounds = default;
                    return false;
                }
                Rect rectangle = sprite.textureRect;
                int width = texture.width;
                int height = texture.height;
                int left = Mathf.RoundToInt(rectangle.xMin);
                int bottom = Mathf.RoundToInt(rectangle.yMin);
                int right = Mathf.RoundToInt(rectangle.xMax);
                int top = Mathf.RoundToInt(rectangle.yMax);
                if (
                    left < 0
                    || bottom < 0
                    || width < right
                    || height < top
                    || right <= left
                    || top <= bottom
                    || 0.001f < Mathf.Abs(rectangle.xMin - left)
                    || 0.001f < Mathf.Abs(rectangle.yMin - bottom)
                    || 0.001f < Mathf.Abs(rectangle.xMax - right)
                    || 0.001f < Mathf.Abs(rectangle.yMax - top)
                )
                {
                    error = "The packed sprite rectangle must cover whole atlas pixels.";
                    bounds = default;
                    return false;
                }
                Vector2[] vertices = sprite.vertices;
                Vector2[] uv = sprite.uv;
                int vertexCount = vertices.Length;
                if (vertexCount < 3 || vertexCount != uv.Length)
                {
                    error = "The packed sprite needs a complete local-to-atlas coordinate mapping.";
                    bounds = default;
                    return false;
                }
                Vector2 origin = vertices[0];
                Vector2 textureOrigin = uv[0];
                double scaleX = 0;
                double scaleY = 0;
                for (int index = 1; index < vertexCount; ++index)
                {
                    double dx = (double)uv[index].x - textureOrigin.x;
                    double dy = (double)uv[index].y - textureOrigin.y;
                    if (scaleX == 0 && dx != 0)
                    {
                        scaleX = ((double)vertices[index].x - origin.x) / dx;
                    }
                    if (scaleY == 0 && dy != 0)
                    {
                        scaleY = ((double)vertices[index].y - origin.y) / dy;
                    }
                }
                if (
                    !(0 < scaleX)
                    || !(0 < scaleY)
                    || double.IsInfinity(scaleX)
                    || double.IsInfinity(scaleY)
                )
                {
                    error = "The packed sprite's local-to-atlas mapping is not representable.";
                    bounds = default;
                    return false;
                }
                for (int index = 0; index < vertexCount; ++index)
                {
                    Vector2 vertex = vertices[index];
                    double x = origin.x + ((double)uv[index].x - textureOrigin.x) * scaleX;
                    double y = origin.y + ((double)uv[index].y - textureOrigin.y) * scaleY;
                    if (
                        !WallMath.IsFinite(vertex)
                        || double.IsNaN(x)
                        || double.IsNaN(y)
                        || 0.0001 * Math.Max(1, Math.Abs(vertex.x)) < Math.Abs(x - vertex.x)
                        || 0.0001 * Math.Max(1, Math.Abs(vertex.y)) < Math.Abs(y - vertex.y)
                    )
                    {
                        error = "The packed sprite does not have an axis-aligned atlas mapping.";
                        bounds = default;
                        return false;
                    }
                }
                Color32[] pixels = texture.GetPixels32();
                if ((long)width * height != pixels.Length)
                {
                    error = "The atlas pixels do not match its dimensions.";
                    bounds = default;
                    return false;
                }
                int drawnLeft = right;
                int drawnBottom = top;
                int drawnRight = -1;
                int drawnTop = -1;
                for (int y = bottom; y < top; ++y)
                {
                    int offset = y * width;
                    for (int x = left; x < right; ++x)
                    {
                        byte alpha = pixels[offset + x].a;
                        if (alpha == 0 || alpha < alphaThreshold)
                        {
                            continue;
                        }
                        drawnLeft = Mathf.Min(drawnLeft, x);
                        drawnBottom = Mathf.Min(drawnBottom, y);
                        drawnRight = Mathf.Max(drawnRight, x);
                        drawnTop = y;
                    }
                }
                if (drawnRight < 0)
                {
                    error = "The packed sprite rectangle is fully transparent.";
                    bounds = default;
                    return false;
                }
                return TryCreateBounds(
                    origin.x + (drawnLeft / (double)width - textureOrigin.x) * scaleX,
                    origin.y + (drawnBottom / (double)height - textureOrigin.y) * scaleY,
                    origin.x + ((drawnRight + 1) / (double)width - textureOrigin.x) * scaleX,
                    origin.y + ((drawnTop + 1) / (double)height - textureOrigin.y) * scaleY,
                    out bounds,
                    out error
                );
            }
            catch (Exception exception)
            {
                error = exception.Message;
                bounds = default;
                return false;
            }
        }
    }
}
