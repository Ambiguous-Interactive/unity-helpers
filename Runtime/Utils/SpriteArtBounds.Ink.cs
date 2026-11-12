// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;

    public static partial class SpriteArtBounds
    {
        /// <summary>Finds the local centroid of the largest connected region of an eligible dark colour.</summary>
        /// <param name="sprite">The sprite to read without changing import settings.</param>
        /// <param name="minimumShare">The minimum fraction of fully opaque pixels belonging to the colour, from zero through one.</param>
        /// <param name="centre">The region's pixel-centre average in local units, or zero on failure.</param>
        /// <param name="error">The refusal reason, or null on success.</param>
        /// <remarks>
        /// Uses source PNG pixels in the Editor or a readable unpacked texture.
        /// Only fully opaque pixels contribute. Colours use integer Rec. 601 weights 299, 587 and 114;
        /// equal luma selects the lowest RGB value. Regions use four-connected exact RGB neighbors;
        /// equal-sized regions select the first bottom-left row-major seed. Coordinates use double
        /// accumulation and the pixel mapping used by the alpha-mask reader.
        /// The connectivity scan remembers rejected pixels instead of rereading them for each neighbor.
        /// </remarks>
        public static bool TryGetCentreOfLargestDarkestInkRegion(
            Sprite sprite,
            float minimumShare,
            out Vector2 centre,
            out string error
        )
        {
            if (!WallMath.IsFinite(minimumShare) || minimumShare < 0 || 1 < minimumShare)
            {
                error = "The minimum opaque pixel share must be finite and between zero and one.";
                centre = default;
                return false;
            }
            try
            {
                if (
                    !SpriteMaskReader.TryReadPixels(
                        sprite,
                        out SpritePixelRegion pixels,
                        out string failure
                    )
                )
                {
                    error = failure;
                    centre = default;
                    return false;
                }
                return TryGetCentreOfLargestDarkestInkRegion(
                    pixels,
                    minimumShare,
                    out centre,
                    out error
                );
            }
            catch (Exception exception)
            {
                error = exception.Message;
                centre = default;
                return false;
            }
        }

        internal static bool TryGetCentreOfLargestDarkestInkRegion(
            SpritePixelRegion pixels,
            float minimumShare,
            out Vector2 centre,
            out string error
        )
        {
            if (
                !WallMath.IsFinite(minimumShare)
                || minimumShare < 0
                || 1 < minimumShare
                || !pixels.TryGetPixelCount(out int count)
            )
            {
                error =
                    "A valid pixel region and a finite minimum share between zero and one are required.";
                centre = default;
                return false;
            }
            using PooledResource<Dictionary<int, int>> countsLease = DictionaryBuffer<
                int,
                int
            >.Dictionary.Get(out Dictionary<int, int> counts);
            int opaque = 0;
            for (int index = 0; index < count; ++index)
            {
                Color32 pixel = pixels.GetPixel(index);
                if (pixel.a != byte.MaxValue)
                {
                    continue;
                }
                ++opaque;
                int rgb = InkRgb(pixel);
                counts[rgb] = counts.TryGetValue(rgb, out int seen) ? seen + 1 : 1;
            }
            if (opaque == 0)
            {
                error = "The sprite region has no fully opaque pixels.";
                centre = default;
                return false;
            }
            float required = minimumShare * opaque;
            int ink = -1;
            int darkest = int.MaxValue;
            foreach (KeyValuePair<int, int> colour in counts)
            {
                if (colour.Value < required)
                {
                    continue;
                }
                int rgb = colour.Key;
                int luma = 299 * (rgb >> 16) + 587 * ((rgb >> 8) & 255) + 114 * (rgb & 255);
                if (luma < darkest || (luma == darkest && rgb < ink))
                {
                    ink = rgb;
                    darkest = luma;
                }
            }
            if (ink < 0)
            {
                error = "No colour meets the minimum opaque pixel share.";
                centre = default;
                return false;
            }
            using PooledArray<bool> reachedLease = SystemArrayPool<bool>.Get(
                count,
                clearArray: true,
                out bool[] reached
            );
            using PooledArray<int> frontierLease = SystemArrayPool<int>.Get(
                count,
                out int[] frontier
            );
            int frontierCount = 0;
            int width = pixels.Region.width;
            int height = pixels.Region.height;
            int largest = 0;
            double centreX = 0;
            double centreY = 0;
            for (int seed = 0; seed < count; ++seed)
            {
                if (!Claim(seed))
                {
                    continue;
                }
                int size = 0;
                double sumX = 0;
                double sumY = 0;
                while (0 < frontierCount)
                {
                    int index = frontier[--frontierCount];
                    int x = index % width;
                    int y = index / width;
                    ++size;
                    sumX += x;
                    sumY += y;
                    if (0 < x)
                    {
                        _ = Claim(index - 1);
                    }
                    if (x < width - 1)
                    {
                        _ = Claim(index + 1);
                    }
                    if (0 < y)
                    {
                        _ = Claim(index - width);
                    }
                    if (y < height - 1)
                    {
                        _ = Claim(index + width);
                    }
                }
                if (largest < size)
                {
                    largest = size;
                    centreX = sumX / size;
                    centreY = sumY / size;
                }
            }
            Vector2 pivot = pixels.Pivot;
            Vector2 units = pixels.UnitsPerPixel;
            float localX = (float)((centreX + 0.5 - pivot.x) * units.x);
            float localY = (float)((centreY + 0.5 - pivot.y) * units.y);
            Vector2 result = new(localX, localY);
            if (!WallMath.IsFinite(result))
            {
                error = "The ink centre cannot be represented in sprite-local coordinates.";
                centre = default;
                return false;
            }
            error = null;
            centre = result;
            return true;

            bool Claim(int index)
            {
                if (reached[index])
                {
                    return false;
                }
                reached[index] = true;
                Color32 pixel = pixels.GetPixel(index);
                if (pixel.a != byte.MaxValue)
                {
                    return false;
                }
                int rgb = InkRgb(pixel);
                if (rgb != ink)
                {
                    return false;
                }
                frontier[frontierCount++] = index;
                return true;
            }
        }

        private static int InkRgb(Color32 pixel)
        {
            return (pixel.r << 16) | (pixel.g << 8) | pixel.b;
        }
    }
}
