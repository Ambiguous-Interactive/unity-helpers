// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Math
{
    using System;
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;
    using DataStructure;
    using Extension;
    using ProtoBuf;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Represents a line segment defined by two endpoints in 2D space.
    /// </summary>
    /// <example>
    /// <code>
    /// var a = new Line2D(new Vector2(0,0), new Vector2(2,0));
    /// var b = new Line2D(new Vector2(1,-1), new Vector2(1,1));
    /// bool intersects = a.Intersects(b); // true
    /// </code>
    /// </example>
    [Serializable]
    [DataContract]
    [ProtoContract]
    [WProtoContract]
    public readonly partial struct Line2D : IEquatable<Line2D>
    {
        /// <summary>
        /// The starting point of the line segment.
        /// </summary>
        [DataMember]
        [ProtoMember(1)]
        [WProtoMember(1)]
        public readonly Vector2 from;

        /// <summary>
        /// The ending point of the line segment.
        /// </summary>
        [DataMember]
        [ProtoMember(2)]
        [WProtoMember(2)]
        public readonly Vector2 to;

        /// <summary>
        /// Constructs a line segment from two points.
        /// </summary>
        /// <param name="from">The starting point.</param>
        /// <param name="to">The ending point.</param>
        [JsonConstructor]
        public Line2D(Vector2 from, Vector2 to)
        {
            this.from = from;
            this.to = to;
        }

        /// <summary>
        /// Gets the length of the line segment.
        /// </summary>
        public float Length => Vector2.Distance(from, to);

        /// <summary>
        /// Gets the squared length of the line segment (more performant than Length).
        /// </summary>
        public float LengthSquared
        {
            get
            {
                float dx = to.x - from.x;
                float dy = to.y - from.y;
                return dx * dx + dy * dy;
            }
        }

        /// <summary>
        /// Gets the direction vector from 'from' to 'to' (unnormalized).
        /// </summary>
        public Vector2 Direction => to - from;

        /// <summary>
        /// Gets the normalized direction vector from 'from' to 'to'.
        /// </summary>
        public Vector2 NormalizedDirection => (to - from).normalized;

        /// <summary>
        /// Checks if this line segment intersects with another line segment.
        /// </summary>
        /// <param name="other">The other line segment to test.</param>
        /// <returns>True if the segments intersect, false otherwise.</returns>
        public bool Intersects(Line2D other)
        {
            return UnityExtensions.Intersects(from, to, other.from, other.to);
        }

        /// <summary>
        /// Checks if this line segment intersects with a circle.
        /// </summary>
        /// <param name="circle">The circle to test for intersection.</param>
        /// <returns>True if the line segment intersects or touches the circle.</returns>
        public bool Intersects(Circle circle)
        {
            float distanceSquared = DistanceSquaredToPoint(circle.center);
            float radiusSquared = circle.radius * circle.radius;
            return distanceSquared <= radiusSquared;
        }

        /// <summary>
        /// Attempts to find the intersection point between this line segment and another.
        /// </summary>
        /// <param name="other">The other line segment to test.</param>
        /// <param name="intersection">The intersection point if found.</param>
        /// <returns>True if an intersection point exists, false otherwise (including parallel/collinear cases).</returns>
        public bool TryGetIntersectionPoint(Line2D other, out Vector2 intersection)
        {
            Vector2 d1 = to - from;
            Vector2 d2 = other.to - other.from;

            float determinant = d1.x * d2.y - d1.y * d2.x;

            if (Mathf.Approximately(determinant, 0))
            {
                intersection = default;
                return false;
            }

            Vector2 diff = other.from - from;

            float t1 = (diff.x * d2.y - diff.y * d2.x) / determinant;
            float t2 = (diff.x * d1.y - diff.y * d1.x) / determinant;

            if (t1 is >= 0 and <= 1 && t2 is >= 0 and <= 1)
            {
                intersection = from + t1 * d1;
                return true;
            }

            intersection = default;
            return false;
        }

        /// <summary>
        /// Calculates the shortest distance from a point to this line segment.
        /// </summary>
        /// <param name="point">The point to measure distance from.</param>
        /// <returns>The shortest distance from the point to the line segment.</returns>
        public float DistanceToPoint(Vector2 point)
        {
            Vector2 closestPoint = ClosestPointOnLine(point);
            return Vector2.Distance(point, closestPoint);
        }

        /// <summary>
        /// Calculates the squared distance from a point to this line segment.
        /// More performant than DistanceToPoint when only comparing distances.
        /// </summary>
        /// <param name="point">The point to measure distance from.</param>
        /// <returns>The squared distance from the point to the line segment.</returns>
        public float DistanceSquaredToPoint(Vector2 point)
        {
            Vector2 closestPoint = ClosestPointOnLine(point);
            return (point - closestPoint).sqrMagnitude;
        }

        /// <summary>
        /// Calculates the shortest distance from a circle to this line segment.
        /// Returns 0 if the line intersects the circle.
        /// </summary>
        /// <param name="circle">The circle to measure distance from.</param>
        /// <returns>The shortest distance from the circle's edge to the line segment.</returns>
        public float DistanceToCircle(Circle circle)
        {
            float distanceToCenter = DistanceToPoint(circle.center);
            return Mathf.Max(0f, distanceToCenter - circle.radius);
        }

        /// <summary>
        /// Finds the closest point on this line segment to the given point.
        /// </summary>
        /// <param name="point">The point to project onto the line.</param>
        /// <returns>The closest point on the line segment.</returns>
        public Vector2 ClosestPointOnLine(Vector2 point)
        {
            Vector2 dir = to - from;
            float lengthSq = dir.sqrMagnitude;

            if (Mathf.Approximately(lengthSq, 0))
            {
                return from;
            }

            float t = Vector2.Dot(point - from, dir) / lengthSq;
            t = Mathf.Clamp01(t);

            return from + t * dir;
        }

        /// <summary>
        /// Checks if a point lies on this line segment (within floating point tolerance).
        /// </summary>
        /// <param name="point">The point to check.</param>
        /// <returns>True if the point lies on the line segment, false otherwise.</returns>
        public bool Contains(Vector2 point)
        {
            Vector2 toPoint = point - from;
            Vector2 toEnd = to - from;
            float cross = toPoint.x * toEnd.y - toPoint.y * toEnd.x;

            if (!Mathf.Approximately(cross, 0))
            {
                return false;
            }

            return UnityExtensions.LiesOnSegment(from, point, to);
        }

        /// <summary>
        /// Checks if this line is equal to another line.
        /// Two lines are equal when their endpoints match exactly, in the same order. Unity's
        /// <c>Vector2</c> <c>==</c> is an approximate comparison and its hash is not, so exact
        /// comparison is what keeps equal lines in the same hash bucket.
        /// </summary>
        public bool Equals(Line2D other)
        {
            return from.Equals(other.from) && to.Equals(other.to);
        }

        /// <summary>
        /// Checks whether both endpoints sit within <paramref name="tolerance"/> of another line's,
        /// in the same order.
        /// </summary>
        /// <param name="other">The other line to compare.</param>
        /// <param name="tolerance">Maximum permitted per-component difference, and the whole of it: nothing relative to the magnitudes is added. Must be finite and non-negative.</param>
        /// <returns>
        /// True when both endpoints agree within <paramref name="tolerance"/>; false when
        /// <paramref name="tolerance"/> is negative, infinite, or not a number. A non-finite
        /// coordinate compares exactly, so two identical infinite endpoints are approximately equal
        /// and this stays reflexive for every line.
        /// </returns>
        public bool ApproximatelyEquals(Line2D other, float tolerance)
        {
            if (float.IsNaN(tolerance) || float.IsInfinity(tolerance) || tolerance < 0f)
            {
                return false;
            }

            return WallMath.WithinTolerance(from, other.from, tolerance)
                && WallMath.WithinTolerance(to, other.to, tolerance);
        }

        /// <summary>
        /// Checks if this line is equal to another object. Only another <see cref="Line2D"/> can be
        /// equal to a line.
        /// </summary>
        public override bool Equals(object obj)
        {
            return obj is Line2D other && Equals(other);
        }

        /// <summary>
        /// Gets the hash code for this line, derived from exactly the members
        /// <see cref="Equals(Line2D)"/> compares.
        /// </summary>
        public override int GetHashCode()
        {
            return Objects.HashCode(from, to);
        }

        /// <summary>
        /// Returns a string representation of this line.
        /// </summary>
        public override string ToString()
        {
            return $"Line2D(from: {from}, to: {to})";
        }

        /// <summary>
        /// Equality operator.
        /// </summary>
        public static bool operator ==(Line2D left, Line2D right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Inequality operator.
        /// </summary>
        public static bool operator !=(Line2D left, Line2D right)
        {
            return !left.Equals(right);
        }
    }
}
