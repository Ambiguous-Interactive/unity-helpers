// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using UnityEngine;

    /// <summary>Compares golden images with an optional edge-peak tolerance.</summary>
    /// <remarks>
    /// Renderers disagree at high-contrast edges. The edge-tolerant overload forgives a mismatched
    /// local peak when its channel error stays within a fraction of that peak's contrast.
    /// </remarks>
    public static class GoldenImage
    {
        /// <summary>Counts pixels whose largest channel difference exceeds a tolerance.</summary>
        /// <param name="expected">The reference pixels.</param>
        /// <param name="actual">The observed pixels.</param>
        /// <param name="tolerance">Largest allowed per-channel difference; negative values are treated as zero.</param>
        /// <returns>The mismatch count, including the length difference.</returns>
        public static int CountMismatches(Color32[] expected, Color32[] actual, int tolerance)
        {
            int expectedLength = expected?.Length ?? 0;
            int actualLength = actual?.Length ?? 0;
            int mismatches = Math.Abs(expectedLength - actualLength);
            int common = Math.Min(expectedLength, actualLength);
            int allowed = Math.Max(0, tolerance);
            for (int index = 0; index < common; ++index)
            {
                if (allowed < MaxChannelDifference(expected[index], actual[index]))
                {
                    ++mismatches;
                }
            }

            return mismatches;
        }

        /// <summary>Counts mismatches while forgiving high-contrast edge peaks.</summary>
        /// <param name="expected">The reference pixels, laid out row-major.</param>
        /// <param name="actual">The observed pixels, laid out row-major.</param>
        /// <param name="tolerance">Largest allowed per-channel difference; negative values are treated as zero.</param>
        /// <param name="width">Pixels per row. Values below one fall back to <see cref="CountMismatches"/>.</param>
        /// <param name="edgePeakFraction">Allowed share of an edge peak's contrast, from zero to one.</param>
        /// <returns>The mismatch count, including the length difference.</returns>
        public static int CountEdgeTolerantMismatches(
            Color32[] expected,
            Color32[] actual,
            int tolerance,
            int width,
            float edgePeakFraction
        )
        {
            int expectedLength = expected?.Length ?? 0;
            return CountEdgeTolerantMismatches(
                expected,
                actual,
                tolerance,
                width,
                width <= 0 ? 1 : (expectedLength + width - 1) / width,
                edgePeakFraction
            );
        }

        /// <summary>Counts mismatches while forgiving high-contrast edge peaks.</summary>
        /// <param name="expected">The reference pixels, laid out row-major.</param>
        /// <param name="actual">The observed pixels, laid out row-major.</param>
        /// <param name="tolerance">Largest allowed per-channel difference; negative values are treated as zero.</param>
        /// <param name="width">Pixels per row.</param>
        /// <param name="height">Pixels per column.</param>
        /// <param name="edgePeakFraction">Allowed share of an edge peak's contrast, from zero to one.</param>
        /// <returns>The mismatch count, including the length difference.</returns>
        public static int CountEdgeTolerantMismatches(
            Color32[] expected,
            Color32[] actual,
            int tolerance,
            int width,
            int height,
            float edgePeakFraction
        )
        {
            if (width <= 0 || height <= 0)
            {
                return CountMismatches(expected, actual, tolerance);
            }

            int expectedLength = expected?.Length ?? 0;
            int actualLength = actual?.Length ?? 0;
            int mismatches = Math.Abs(expectedLength - actualLength);
            int common = Math.Min(expectedLength, actualLength);
            int allowed = Math.Max(0, tolerance);
            float fraction = Mathf.Clamp01(edgePeakFraction);
            for (int index = 0; index < common; ++index)
            {
                int difference = MaxChannelDifference(expected[index], actual[index]);
                if (difference <= allowed)
                {
                    continue;
                }

                if (TryGetEdgeAllowance(expected, index, width, height, out int allowance))
                {
                    if (difference <= fraction * allowance)
                    {
                        continue;
                    }
                }

                ++mismatches;
            }

            return mismatches;
        }

        private static bool TryGetEdgeAllowance(
            Color32[] image,
            int index,
            int width,
            int height,
            out int contrast
        )
        {
            if (image == null || index < 0 || image.Length <= index)
            {
                contrast = 0;
                return false;
            }

            int x = index % width;
            int y = index / width;
            if (height <= y)
            {
                contrast = 0;
                return false;
            }

            Color32 center = image[index];
            int centerLuma = Luma(center);
            bool brighterThanEveryNeighbor = true;
            bool darkerThanEveryNeighbor = true;
            bool hasNeighbor = false;
            int foundContrast = 0;
            for (int offsetY = -1; offsetY <= 1; ++offsetY)
            {
                int neighborY = y + offsetY;
                if (neighborY < 0 || height <= neighborY)
                {
                    continue;
                }

                for (int offsetX = -1; offsetX <= 1; ++offsetX)
                {
                    int neighborX = x + offsetX;
                    if (neighborX < 0 || width <= neighborX || (offsetX == 0 && offsetY == 0))
                    {
                        continue;
                    }

                    long neighborIndex = (long)neighborY * width + neighborX;
                    if (neighborIndex < 0 || image.Length <= neighborIndex)
                    {
                        continue;
                    }

                    hasNeighbor = true;
                    int neighborLuma = Luma(image[(int)neighborIndex]);
                    int difference = Math.Abs(centerLuma - neighborLuma);
                    if (foundContrast < difference)
                    {
                        foundContrast = difference;
                    }

                    if (centerLuma <= neighborLuma)
                    {
                        brighterThanEveryNeighbor = false;
                    }

                    if (neighborLuma <= centerLuma)
                    {
                        darkerThanEveryNeighbor = false;
                    }
                }
            }

            contrast = foundContrast;
            return hasNeighbor && (brighterThanEveryNeighbor || darkerThanEveryNeighbor);
        }

        private static int MaxChannelDifference(Color32 first, Color32 second)
        {
            int red = Math.Abs(first.r - second.r);
            int green = Math.Abs(first.g - second.g);
            int blue = Math.Abs(first.b - second.b);
            int alpha = Math.Abs(first.a - second.a);
            int largest = red;
            if (largest < green)
            {
                largest = green;
            }
            if (largest < blue)
            {
                largest = blue;
            }
            if (largest < alpha)
            {
                largest = alpha;
            }
            return largest;
        }

        private static int Luma(Color32 color)
        {
            return (color.r * 299 + color.g * 587 + color.b * 114) / 1000;
        }
    }
}
