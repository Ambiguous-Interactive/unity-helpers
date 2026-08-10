// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Math
{
    using System;
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;
    using DataStructure;
    using ProtoBuf;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Represents a line segment defined by two endpoints in 3D space.
    /// </summary>
    [Serializable]
    [DataContract]
    [ProtoContract]
    [WProtoContract]
    public readonly partial struct Line3D : IEquatable<Line3D>
    {
        /// <summary>
        /// The starting point of the line segment.
        /// </summary>
        [DataMember]
        [ProtoMember(1)]
        [WProtoMember(1)]
        public readonly Vector3 from;

        /// <summary>
        /// The ending point of the line segment.
        /// </summary>
        [DataMember]
        [ProtoMember(2)]
        [WProtoMember(2)]
        public readonly Vector3 to;

        /// <summary>
        /// Constructs a line segment from two points.
        /// </summary>
        /// <param name="from">The starting point.</param>
        /// <param name="to">The ending point.</param>
        [JsonConstructor]
        public Line3D(Vector3 from, Vector3 to)
        {
            this.from = from;
            this.to = to;
        }

        /// <summary>
        /// Gets the length of the line segment.
        /// </summary>
        public float Length => Vector3.Distance(from, to);

        /// <summary>
        /// Gets the squared length of the line segment (more performant than Length).
        /// </summary>
        public float LengthSquared
        {
            get
            {
                float dx = to.x - from.x;
                float dy = to.y - from.y;
                float dz = to.z - from.z;
                return dx * dx + dy * dy + dz * dz;
            }
        }

        /// <summary>
        /// Gets the direction vector from 'from' to 'to' (unnormalized).
        /// </summary>
        public Vector3 Direction => to - from;

        /// <summary>
        /// Gets the normalized direction vector from 'from' to 'to'.
        /// </summary>
        public Vector3 NormalizedDirection => (to - from).normalized;

        /// <summary>
        /// Checks if this line segment intersects with a sphere.
        /// </summary>
        /// <param name="sphere">The sphere to test for intersection.</param>
        /// <returns>True if the line segment intersects or touches the sphere.</returns>
        public bool Intersects(Sphere sphere)
        {
            float distanceSquared = DistanceSquaredToPoint(sphere.center);
            float radiusSquared = sphere.radius * sphere.radius;
            return distanceSquared <= radiusSquared;
        }

        /// <summary>
        /// Checks if this line segment intersects with a bounding box.
        /// </summary>
        /// <param name="bounds">The bounding box to test for intersection.</param>
        /// <returns>True if the line segment intersects the bounding box.</returns>
        public bool Intersects(BoundingBox3D bounds)
        {
            if (bounds.IsEmpty)
            {
                return false;
            }

            return TryClipSegmentAABB(bounds, out _, out _);
        }

        /// <summary>
        /// Finds the closest points between this line segment and another line segment.
        /// For skew lines (lines that don't intersect and aren't parallel), this finds the unique closest pair.
        /// </summary>
        /// <param name="other">The other line segment.</param>
        /// <param name="thisClosest">The closest point on this line segment.</param>
        /// <param name="otherClosest">The closest point on the other line segment.</param>
        /// <returns>True if the lines are not parallel, false if they are parallel or nearly parallel.</returns>
        public bool TryGetClosestPoints(
            Line3D other,
            out Vector3 thisClosest,
            out Vector3 otherClosest
        )
        {
            Vector3 d1 = Direction;
            Vector3 d2 = other.Direction;
            Vector3 r = from - other.from;

            float a = Vector3.Dot(d1, d1);
            float b = Vector3.Dot(d1, d2);
            float c = Vector3.Dot(d1, r);
            float e = Vector3.Dot(d2, d2);
            float f = Vector3.Dot(d2, r);

            float denom = a * e - b * b;

            if (Mathf.Approximately(denom, 0))
            {
                thisClosest = from;
                otherClosest = other.ClosestPointOnLine(from);
                return false;
            }

            float s = Mathf.Clamp01((b * f - c * e) / denom);
            float t = Mathf.Clamp01((a * f - b * c) / denom);

            thisClosest = from + s * d1;
            otherClosest = other.from + t * d2;
            return true;
        }

        /// <summary>
        /// Calculates the shortest distance between this line segment and another line segment.
        /// </summary>
        /// <param name="other">The other line segment.</param>
        /// <returns>The shortest distance between the two line segments.</returns>
        public float DistanceToLine(Line3D other)
        {
            TryGetClosestPoints(other, out Vector3 thisClosest, out Vector3 otherClosest);
            return Vector3.Distance(thisClosest, otherClosest);
        }

        /// <summary>
        /// Calculates the shortest distance from a point to this line segment.
        /// </summary>
        /// <param name="point">The point to measure distance from.</param>
        /// <returns>The shortest distance from the point to the line segment.</returns>
        public float DistanceToPoint(Vector3 point)
        {
            Vector3 closestPoint = ClosestPointOnLine(point);
            return Vector3.Distance(point, closestPoint);
        }

        /// <summary>
        /// Calculates the squared distance from a point to this line segment.
        /// More performant than DistanceToPoint when only comparing distances.
        /// </summary>
        /// <param name="point">The point to measure distance from.</param>
        /// <returns>The squared distance from the point to the line segment.</returns>
        public float DistanceSquaredToPoint(Vector3 point)
        {
            Vector3 closestPoint = ClosestPointOnLine(point);
            return (point - closestPoint).sqrMagnitude;
        }

        /// <summary>
        /// Calculates the shortest distance from a sphere to this line segment.
        /// Returns 0 if the line intersects the sphere.
        /// </summary>
        /// <param name="sphere">The sphere to measure distance from.</param>
        /// <returns>The shortest distance from the sphere's surface to the line segment.</returns>
        public float DistanceToSphere(Sphere sphere)
        {
            float distanceToCenter = DistanceToPoint(sphere.center);
            return Mathf.Max(0f, distanceToCenter - sphere.radius);
        }

        /// <summary>
        /// Calculates the shortest distance from a bounding box to this line segment.
        /// Returns 0 if the line intersects the bounding box.
        /// </summary>
        /// <param name="bounds">The bounding box to measure distance from.</param>
        /// <returns>The shortest distance from the bounding box to the line segment.</returns>
        public float DistanceToBounds(BoundingBox3D bounds)
        {
            if (bounds.IsEmpty)
            {
                return float.PositiveInfinity;
            }

            Vector3 closestOnLine = ClosestPointOnBounds(bounds);
            Vector3 closestOnBounds = ClampToBounds(closestOnLine, bounds);
            return Vector3.Distance(closestOnLine, closestOnBounds);
        }

        /// <summary>
        /// Finds the closest point on this line segment to the given point.
        /// </summary>
        /// <param name="point">The point to project onto the line.</param>
        /// <returns>The closest point on the line segment.</returns>
        public Vector3 ClosestPointOnLine(Vector3 point)
        {
            Vector3 dir = to - from;
            float lengthSq = dir.sqrMagnitude;

            if (Mathf.Approximately(lengthSq, 0))
            {
                return from;
            }

            float t = Vector3.Dot(point - from, dir) / lengthSq;
            t = Mathf.Clamp01(t);

            return from + t * dir;
        }

        /// <summary>
        /// Finds the closest point on this line segment to a bounding box.
        /// </summary>
        /// <param name="bounds">The bounding box.</param>
        /// <returns>The closest point on the line segment to the bounding box.</returns>
        public Vector3 ClosestPointOnBounds(BoundingBox3D bounds)
        {
            if (bounds.IsEmpty)
            {
                return from;
            }

            // If it intersects, return the first intersection point along the segment.
            if (TryClipSegmentAABB(bounds, out float tEnter, out _))
            {
                float tHit = Mathf.Clamp01(tEnter);
                return from + (to - from) * tHit;
            }

            // Otherwise, find the exact closest point on the segment to the AABB using
            // convex 1D minimization of f(t) = ||p(t) - clamp(p(t))||^2 over t in [0,1].
            Vector3 d = to - from;
            float lenSq = d.sqrMagnitude;
            if (lenSq <= 1e-20f)
            {
                return from;
            }

            Vector3 localFrom = from;

            float g0 = G(0f);
            float g1 = G(1f);
            if (g0 >= 0f)
            {
                return from;
            }
            if (g1 <= 0f)
            {
                return to;
            }

            float a = 0f;
            float b = 1f;
            for (int i = 0; i < 50; i++)
            {
                float m = 0.5f * (a + b);
                float gm = G(m);
                if (gm > 0f)
                {
                    b = m;
                }
                else
                {
                    a = m;
                }
            }
            float tStar = 0.5f * (a + b);
            return from + d * tStar;

            float G(float t)
            {
                Vector3 p = localFrom + d * t;
                Vector3 c = ClampToBounds(p, bounds);
                Vector3 diff = p - c;
                return diff.x * d.x + diff.y * d.y + diff.z * d.z;
            }
        }

        private static Vector3 ClampToBounds(Vector3 p, BoundingBox3D bounds)
        {
            return new Vector3(
                Mathf.Clamp(p.x, bounds.min.x, bounds.max.x),
                Mathf.Clamp(p.y, bounds.min.y, bounds.max.y),
                Mathf.Clamp(p.z, bounds.min.z, bounds.max.z)
            );
        }

        private bool TryClipSegmentAABB(BoundingBox3D bounds, out float tEnter, out float tExit)
        {
            Vector3 d = to - from;

            tEnter = 0f;
            tExit = 1f;

            // X axis
            if (Mathf.Abs(d.x) < 1e-8f)
            {
                if (from.x < bounds.min.x || from.x > bounds.max.x)
                {
                    return false;
                }
            }
            else
            {
                float inv = 1f / d.x;
                float t1 = (bounds.min.x - from.x) * inv;
                float t2 = (bounds.max.x - from.x) * inv;
                if (t1 > t2)
                {
                    (t1, t2) = (t2, t1);
                }
                tEnter = Mathf.Max(tEnter, t1);
                tExit = Mathf.Min(tExit, t2);
                if (tEnter > tExit)
                {
                    return false;
                }
            }

            // Y axis
            if (Mathf.Abs(d.y) < 1e-8f)
            {
                if (from.y < bounds.min.y || from.y > bounds.max.y)
                {
                    return false;
                }
            }
            else
            {
                float inv = 1f / d.y;
                float t1 = (bounds.min.y - from.y) * inv;
                float t2 = (bounds.max.y - from.y) * inv;
                if (t1 > t2)
                {
                    (t1, t2) = (t2, t1);
                }
                tEnter = Mathf.Max(tEnter, t1);
                tExit = Mathf.Min(tExit, t2);
                if (tEnter > tExit)
                {
                    return false;
                }
            }

            // Z axis
            if (Mathf.Abs(d.z) < 1e-8f)
            {
                if (from.z < bounds.min.z || from.z > bounds.max.z)
                {
                    return false;
                }
            }
            else
            {
                float inv = 1f / d.z;
                float t1 = (bounds.min.z - from.z) * inv;
                float t2 = (bounds.max.z - from.z) * inv;
                if (t1 > t2)
                {
                    (t1, t2) = (t2, t1);
                }
                tEnter = Mathf.Max(tEnter, t1);
                tExit = Mathf.Min(tExit, t2);
                if (tEnter > tExit)
                {
                    return false;
                }
            }

            return tExit >= 0f && tEnter <= 1f && tExit >= tEnter;
        }

        /// <summary>
        /// Checks if a point lies on this line segment (within a specified tolerance).
        /// </summary>
        /// <param name="point">The point to check.</param>
        /// <param name="tolerance">The maximum distance from the line to consider the point as contained.</param>
        /// <returns>True if the point lies on the line segment within the tolerance, false otherwise.</returns>
        public bool Contains(Vector3 point, float tolerance = 0.0001f)
        {
            Vector3 closestPoint = ClosestPointOnLine(point);
            return Vector3.Distance(point, closestPoint) <= tolerance;
        }

        /// <summary>
        /// Checks if this line is equal to another line.
        /// Two lines are equal if they have the same endpoints (in the same order).
        /// </summary>
        public bool Equals(Line3D other)
        {
            return from == other.from && to == other.to;
        }

        /// <summary>
        /// Checks if this line is equal to another object.
        /// </summary>
        public override bool Equals(object obj)
        {
            return obj is Line3D other && Equals(other);
        }

        /// <summary>
        /// Gets the hash code for this line.
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
            return $"Line3D(from: {from}, to: {to})";
        }

        /// <summary>
        /// Equality operator.
        /// </summary>
        public static bool operator ==(Line3D left, Line3D right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Inequality operator.
        /// </summary>
        public static bool operator !=(Line3D left, Line3D right)
        {
            return !left.Equals(right);
        }
    }
}
