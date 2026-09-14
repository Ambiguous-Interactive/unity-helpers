// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Generates evenly distributed points along circular curves and arcs.
    /// </summary>
    public static class ShapeHelper
    {
        /// <summary>
        /// Generates points from <paramref name="startAngle"/> through <paramref name="endAngle"/>,
        /// expressed in degrees.
        /// </summary>
        /// <param name="center">The center of the circle.</param>
        /// <param name="radius">The positive circle radius.</param>
        /// <param name="startAngle">The first angle, in degrees.</param>
        /// <param name="endAngle">The last angle, in degrees.</param>
        /// <param name="pointCount">The positive number of points to generate.</param>
        /// <param name="buffer">An optional list to clear and reuse.</param>
        /// <returns>The supplied <paramref name="buffer"/>, or a newly allocated list.</returns>
        public static List<Vector2> GenerateCircularCurvePoints(
            Vector2 center,
            float radius,
            float startAngle,
            float endAngle,
            int pointCount,
            List<Vector2> buffer = null
        )
        {
            if (
                pointCount <= 0
                || !IsFinitePositive(radius)
                || !IsFinite(center.x)
                || !IsFinite(center.y)
                || !IsFinite(startAngle)
                || !IsFinite(endAngle)
            )
            {
                buffer ??= new List<Vector2>();
                buffer.Clear();
                return buffer;
            }

            buffer ??= new List<Vector2>(pointCount);
            buffer.Clear();
            if (buffer.Capacity < pointCount)
            {
                buffer.Capacity = pointCount;
            }

            double angleRange = (double)endAngle - startAngle;
            for (int i = 0; i < pointCount; ++i)
            {
                double interpolation = pointCount == 1 ? 0.5 : (double)i / (pointCount - 1);
                float angleRadians = (float)(
                    (startAngle + angleRange * interpolation) * Mathf.Deg2Rad
                );
                buffer.Add(
                    center
                        + new Vector2(
                            radius * Mathf.Cos(angleRadians),
                            radius * Mathf.Sin(angleRadians)
                        )
                );
            }

            return buffer;
        }

        /// <summary>
        /// Generates a fraction of the upper semicircle, centered on 90 degrees.
        /// </summary>
        /// <param name="center">The center of the circle.</param>
        /// <param name="radius">The positive circle radius.</param>
        /// <param name="fraction">The fraction of the upper semicircle in (0, 1].</param>
        /// <param name="pointCount">The positive number of points to generate.</param>
        /// <param name="buffer">An optional list to clear and reuse.</param>
        /// <returns>The supplied <paramref name="buffer"/>, or a newly allocated list.</returns>
        public static List<Vector2> GenerateTopCircularFraction(
            Vector2 center,
            float radius,
            float fraction,
            int pointCount,
            List<Vector2> buffer = null
        )
        {
            if (!IsValidFraction(fraction))
            {
                buffer ??= new List<Vector2>();
                buffer.Clear();
                return buffer;
            }

            float arcAngle = 180f * fraction;
            return GenerateCircularCurvePoints(
                center,
                radius,
                90f - arcAngle * 0.5f,
                90f + arcAngle * 0.5f,
                pointCount,
                buffer
            );
        }

        /// <summary>
        /// Generates a fraction of a full circle centered on <paramref name="centerAngle"/>.
        /// </summary>
        /// <param name="center">The center of the circle.</param>
        /// <param name="radius">The positive circle radius.</param>
        /// <param name="fraction">The fraction of the full circle in (0, 1].</param>
        /// <param name="centerAngle">The center of the arc, in degrees.</param>
        /// <param name="pointCount">The positive number of points to generate.</param>
        /// <param name="buffer">An optional list to clear and reuse.</param>
        /// <returns>The supplied <paramref name="buffer"/>, or a newly allocated list.</returns>
        public static List<Vector2> GenerateCircularFraction(
            Vector2 center,
            float radius,
            float fraction,
            float centerAngle,
            int pointCount,
            List<Vector2> buffer = null
        )
        {
            if (!IsValidFraction(fraction) || !IsFinite(centerAngle))
            {
                buffer ??= new List<Vector2>();
                buffer.Clear();
                return buffer;
            }

            float arcAngle = 360f * fraction;
            return GenerateCircularCurvePoints(
                center,
                radius,
                centerAngle - arcAngle * 0.5f,
                centerAngle + arcAngle * 0.5f,
                pointCount,
                buffer
            );
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinitePositive(float value)
        {
            return 0 < value && IsFinite(value);
        }

        private static bool IsValidFraction(float fraction)
        {
            return 0 < fraction && fraction <= 1 && IsFinite(fraction);
        }
    }
}
