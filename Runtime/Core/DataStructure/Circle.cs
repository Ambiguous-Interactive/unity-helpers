// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Text.Json.Serialization;
    using Extension;
    using Math;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>
    /// Lightweight 2D circle utility that powers overlap tests and distance checks for gameplay zones, detection ranges, and radial falloffs.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// Circle aggroRange = new Circle(enemyPosition, 5f);
    /// if (aggroRange.Contains(playerPosition))
    /// {
    ///     enemy.BeginChase();
    /// }
    /// ]]></code>
    /// </example>
    public readonly struct Circle : IEquatable<Circle>
    {
        public readonly Vector2 center;
        public readonly float radius;
        private readonly float _radiusSquared;

        /// <summary>
        /// Initializes a new circle with the specified center and radius.
        /// </summary>
        /// <param name="center">The center point of the circle.</param>
        /// <param name="radius">The radius of the circle.</param>
        [JsonConstructor]
        public Circle(Vector2 center, float radius)
        {
            this.center = center;
            this.radius = radius;
            _radiusSquared = radius * radius;
        }

        /// <summary>
        /// Determines whether the circle contains the specified point.
        /// Points on the circumference are considered contained.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns>True if the point is inside or on the circle's circumference.</returns>
        public bool Contains(Vector2 point)
        {
            return (center - point).sqrMagnitude <= _radiusSquared;
        }

        /// <summary>
        /// Determines whether this circle intersects with the specified bounds.
        /// Returns true if there is any overlap between the circle and bounds.
        /// </summary>
        /// <param name="bounds">The bounds to test for intersection.</param>
        /// <returns>True if the circle and bounds intersect.</returns>
        public bool Intersects(Bounds bounds)
        {
            return Intersects(bounds.Rect());
        }

        /// <summary>
        /// Determines whether this circle intersects with the specified rectangle.
        /// Returns true if there is any overlap between the circle and rectangle.
        /// </summary>
        /// <param name="rectangle">The rectangle to test for intersection.</param>
        /// <returns>True if the circle and rectangle intersect.</returns>
        // https://www.geeksforgeeks.org/check-if-any-point-overlaps-the-given-circle-and-rectangle/
        public bool Intersects(Rect rectangle)
        {
            float xN = Mathf.Clamp(center.x, rectangle.xMin, rectangle.xMax);
            float yN = Mathf.Clamp(center.y, rectangle.yMin, rectangle.yMax);
            float dX = xN - center.x;
            float dY = yN - center.y;
            // Add a tiny tolerance to account for floating-point rounding when touching exactly at an edge/corner
            const float Tolerance = 1e-6f;
            return (dX * dX + dY * dY) <= (_radiusSquared + Tolerance);
        }

        /// <summary>
        /// Determines whether this circle intersects with another circle.
        /// Returns true if there is any overlap between the two circles.
        /// </summary>
        /// <param name="other">The other circle to test for intersection.</param>
        /// <returns>True if the circles intersect.</returns>
        public bool Intersects(Circle other)
        {
            float combinedRadius = radius + other.radius;
            float combinedRadiusSquared = combinedRadius * combinedRadius;
            return (center - other.center).sqrMagnitude <= combinedRadiusSquared;
        }

        /// <summary>
        /// Determines whether this circle intersects with a line segment.
        /// Returns true if the line segment intersects or touches the circle.
        /// </summary>
        /// <param name="line">The line segment to test for intersection.</param>
        /// <returns>True if the line segment intersects the circle.</returns>
        public bool Intersects(Line2D line)
        {
            return line.Intersects(this);
        }

        /// <summary>
        /// Calculates the shortest distance from this circle to a line segment.
        /// Returns 0 if the line intersects the circle.
        /// </summary>
        /// <param name="line">The line segment to measure distance from.</param>
        /// <returns>The shortest distance from the circle's edge to the line segment.</returns>
        public float DistanceToLine(Line2D line)
        {
            return line.DistanceToCircle(this);
        }

        /// <summary>
        /// Finds the closest point on a line segment to this circle's center.
        /// </summary>
        /// <param name="line">The line segment.</param>
        /// <returns>The closest point on the line segment to the circle's center.</returns>
        public Vector2 ClosestPointOnLine(Line2D line)
        {
            return line.ClosestPointOnLine(center);
        }

        /// <summary>
        /// Determines whether the specified bounds are completely contained within this circle.
        /// All corners of the bounds must be inside the circle.
        /// </summary>
        /// <param name="bounds">The bounds to test for containment.</param>
        /// <returns>True if the bounds are completely contained within the circle.</returns>
        public bool Overlaps(Bounds bounds)
        {
            return Overlaps(bounds.Rect());
        }

        /// <summary>
        /// Determines whether the specified rectangle is completely contained within this circle.
        /// All four corners of the rectangle must be inside the circle.
        /// </summary>
        /// <param name="rectangle">The rectangle to test for containment.</param>
        /// <returns>True if the rectangle is completely contained within the circle.</returns>
        public bool Overlaps(Rect rectangle)
        {
            // Containment reduces to the farthest corner of an axis-aligned rectangle.
            Vector2 min = rectangle.min;
            Vector2 max = rectangle.max;

            return Contains(min)
                && Contains(max)
                && Contains(new Vector2(min.x, max.y))
                && Contains(new Vector2(max.x, min.y));
        }

        /// <summary>
        /// Determines whether this circle equals another circle. The center and the radius are both
        /// compared exactly, so every pair this reports equal also shares a hash code.
        /// </summary>
        /// <param name="other">The other circle to compare.</param>
        /// <returns>True if the circles have exactly the same center and radius.</returns>
        public bool Equals(Circle other)
        {
            return center.Equals(other.center) && radius.Equals(other.radius);
        }

        /// <summary>
        /// Determines whether this circle sits within <paramref name="tolerance"/> of another circle
        /// on every component. Reach for this instead of <see cref="Equals(Circle)"/> when comparing
        /// circles that were computed rather than authored.
        /// </summary>
        /// <param name="other">The other circle to compare.</param>
        /// <param name="tolerance">Maximum permitted per-component difference, and the whole of it: nothing relative to the magnitudes is added. Must be finite and non-negative.</param>
        /// <returns>
        /// True when the centers and the radii each agree within <paramref name="tolerance"/>;
        /// false when <paramref name="tolerance"/> is negative, infinite, or not a number. A
        /// non-finite component compares exactly, so two identical infinite radii are approximately
        /// equal and this stays reflexive for every circle.
        /// </returns>
        public bool ApproximatelyEquals(Circle other, float tolerance)
        {
            if (float.IsNaN(tolerance) || float.IsInfinity(tolerance) || tolerance < 0f)
            {
                return false;
            }

            return WallMath.WithinTolerance(center, other.center, tolerance)
                && WallMath.WithinTolerance(radius, other.radius, tolerance);
        }

        /// <summary>
        /// Determines whether this circle equals another object. Only another <see cref="Circle"/>
        /// can be equal to a circle.
        /// </summary>
        /// <param name="obj">The object to compare.</param>
        /// <returns>True if the object is a Circle with exactly the same center and radius.</returns>
        public override bool Equals(object obj)
        {
            return obj is Circle other && Equals(other);
        }

        /// <summary>
        /// Gets the hash code for this circle, derived from exactly the members
        /// <see cref="Equals(Circle)"/> compares.
        /// </summary>
        /// <returns>A hash code for the current circle.</returns>
        public override int GetHashCode()
        {
            return Objects.HashCode(center, radius);
        }

        /// <summary>
        /// Determines whether two circles are equal.
        /// </summary>
        public static bool operator ==(Circle left, Circle right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether two circles are not equal.
        /// </summary>
        public static bool operator !=(Circle left, Circle right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Returns a string representation of this circle.
        /// </summary>
        /// <returns>A string describing the circle's center and radius.</returns>
        public override string ToString()
        {
            return $"Circle(center: {center}, radius: {radius})";
        }
    }
}
