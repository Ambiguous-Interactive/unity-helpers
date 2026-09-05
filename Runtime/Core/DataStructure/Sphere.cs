// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Text.Json.Serialization;
    using Helper;
    using Math;
    using UnityEngine;

    /// <summary>
    /// Compact 3D sphere helper for distance checks, containment tests, and broad-phase overlap queries.
    /// Ideal for vision cones, trigger volumes, and physics culling.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// Sphere detection = new Sphere(transform.position, 4f);
    /// bool containsTarget = detection.Contains(targetPosition);
    /// ]]></code>
    /// </example>
    public readonly struct Sphere : IEquatable<Sphere>
    {
        public readonly Vector3 center;
        public readonly float radius;
        private readonly float _radiusSquared;

        /// <summary>
        /// Initializes a new sphere with the specified center and radius.
        /// </summary>
        /// <param name="center">The center point of the sphere.</param>
        /// <param name="radius">The radius of the sphere.</param>
        [JsonConstructor]
        public Sphere(Vector3 center, float radius)
        {
            this.center = center;
            this.radius = radius;
            _radiusSquared = radius * radius;
        }

        /// <summary>
        /// Determines whether the sphere contains the specified point.
        /// Points on the surface are considered contained.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns>True if the point is inside or on the sphere's surface.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(Vector3 point)
        {
            float dx = center.x - point.x;
            float dy = center.y - point.y;
            float dz = center.z - point.z;
            return dx * dx + dy * dy + dz * dz <= _radiusSquared;
        }

        /// <summary>
        /// Determines whether this sphere intersects with the specified Unity Bounds.
        /// Returns true if there is any overlap between the sphere and bounds.
        /// </summary>
        /// <param name="bounds">The Unity Bounds to test for intersection.</param>
        /// <returns>True if the sphere and bounds intersect.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(Bounds bounds)
        {
            return Intersects(BoundingBox3D.FromClosedBounds(bounds));
        }

        /// <summary>
        /// Determines whether this sphere intersects with the specified bounding box.
        /// Returns true if there is any overlap between the sphere and bounds.
        /// </summary>
        /// <param name="bounds">The bounding box to test for intersection.</param>
        /// <returns>True if the sphere and bounds intersect.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(BoundingBox3D bounds)
        {
            Vector3 closest = bounds.ClosestPoint(center);
            float dx = closest.x - center.x;
            float dy = closest.y - center.y;
            float dz = closest.z - center.z;
            float distanceSquared = dx * dx + dy * dy + dz * dz;
            // Add a tiny tolerance to account for floating-point rounding when touching exactly at an edge/corner
            const float Tolerance = 1e-6f;
            return distanceSquared <= (_radiusSquared + Tolerance);
        }

        /// <summary>
        /// Determines whether this sphere intersects with another sphere.
        /// Returns true if there is any overlap between the two spheres.
        /// </summary>
        /// <param name="other">The other sphere to test for intersection.</param>
        /// <returns>True if the spheres intersect.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(Sphere other)
        {
            float combinedRadius = radius + other.radius;
            float combinedRadiusSquared = combinedRadius * combinedRadius;
            float dx = center.x - other.center.x;
            float dy = center.y - other.center.y;
            float dz = center.z - other.center.z;
            return dx * dx + dy * dy + dz * dz <= combinedRadiusSquared;
        }

        /// <summary>
        /// Determines whether this sphere intersects with a line segment.
        /// Returns true if the line segment intersects or touches the sphere.
        /// </summary>
        /// <param name="line">The line segment to test for intersection.</param>
        /// <returns>True if the line segment intersects the sphere.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(Line3D line)
        {
            return line.Intersects(this);
        }

        /// <summary>
        /// Calculates the shortest distance from this sphere to a line segment.
        /// Returns 0 if the line intersects the sphere.
        /// </summary>
        /// <param name="line">The line segment to measure distance from.</param>
        /// <returns>The shortest distance from the sphere's surface to the line segment.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float DistanceToLine(Line3D line)
        {
            return line.DistanceToSphere(this);
        }

        /// <summary>
        /// Finds the closest point on a line segment to this sphere's center.
        /// </summary>
        /// <param name="line">The line segment.</param>
        /// <returns>The closest point on the line segment to the sphere's center.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector3 ClosestPointOnLine(Line3D line)
        {
            return line.ClosestPointOnLine(center);
        }

        /// <summary>
        /// Determines whether the specified Unity Bounds is completely contained within this sphere.
        /// All corners of the bounds must be inside the sphere.
        /// </summary>
        /// <param name="bounds">The Unity Bounds to test for containment.</param>
        /// <returns>True if the bounds is completely contained within the sphere.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Overlaps(Bounds bounds)
        {
            return Overlaps(BoundingBox3D.FromClosedBounds(bounds));
        }

        /// <summary>
        /// Determines whether the specified bounding box is completely contained within this sphere.
        /// All corners of the bounding box must be inside the sphere.
        /// </summary>
        /// <param name="bounds">The bounding box to test for containment.</param>
        /// <returns>True if the bounding box is completely contained within the sphere.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Overlaps(BoundingBox3D bounds)
        {
            // Empty bounds are considered to overlap any sphere
            if (bounds.IsEmpty)
            {
                return true;
            }

            // A nudged half-open point bound must still count as contained at the sphere center.
            float minDx = bounds.min.x - center.x;
            float minDy = bounds.min.y - center.y;
            float minDz = bounds.min.z - center.z;
            float minDistSquared = minDx * minDx + minDy * minDy + minDz * minDz;

            if (minDistSquared <= _radiusSquared)
            {
                float sizeX = bounds.max.x - bounds.min.x;
                float sizeY = bounds.max.y - bounds.min.y;
                float sizeZ = bounds.max.z - bounds.min.z;
                float maxSize =
                    sizeY < sizeX
                        ? (sizeZ < sizeX ? sizeX : sizeZ)
                        : (sizeZ < sizeY ? sizeY : sizeZ);

                if (maxSize < 1e-5f)
                {
                    return true;
                }
            }

            // Containment reduces to the farthest corner of an axis-aligned box.
            float toMinX = bounds.min.x - center.x;
            float toMinY = bounds.min.y - center.y;
            float toMinZ = bounds.min.z - center.z;
            float toMaxX = bounds.max.x - center.x;
            float toMaxY = bounds.max.y - center.y;
            float toMaxZ = bounds.max.z - center.z;

            float absMinX = toMinX < 0 ? -toMinX : toMinX;
            float absMaxX = toMaxX < 0 ? -toMaxX : toMaxX;
            float farthestX = absMaxX < absMinX ? toMinX : toMaxX;

            float absMinY = toMinY < 0 ? -toMinY : toMinY;
            float absMaxY = toMaxY < 0 ? -toMaxY : toMaxY;
            float farthestY = absMaxY < absMinY ? toMinY : toMaxY;

            float absMinZ = toMinZ < 0 ? -toMinZ : toMinZ;
            float absMaxZ = toMaxZ < 0 ? -toMaxZ : toMaxZ;
            float farthestZ = absMaxZ < absMinZ ? toMinZ : toMaxZ;

            float farthestDistanceSquared =
                farthestX * farthestX + farthestY * farthestY + farthestZ * farthestZ;
            return farthestDistanceSquared <= _radiusSquared;
        }

        /// <summary>
        /// Determines whether this sphere equals another sphere. The center and the radius are both
        /// compared exactly, so every pair this reports equal also shares a hash code.
        /// </summary>
        /// <param name="other">The other sphere to compare.</param>
        /// <returns>True if the spheres have exactly the same center and radius.</returns>
        public bool Equals(Sphere other)
        {
            return center.Equals(other.center) && radius.Equals(other.radius);
        }

        /// <summary>
        /// Determines whether this sphere sits within <paramref name="tolerance"/> of another sphere
        /// on every component. Reach for this instead of <see cref="Equals(Sphere)"/> when comparing
        /// spheres that were computed rather than authored.
        /// </summary>
        /// <param name="other">The other sphere to compare.</param>
        /// <param name="tolerance">Maximum permitted per-component difference, and the whole of it: nothing relative to the magnitudes is added. Must be finite and non-negative.</param>
        /// <returns>
        /// True when the centers and the radii each agree within <paramref name="tolerance"/>;
        /// false when <paramref name="tolerance"/> is negative, infinite, or not a number. A
        /// non-finite component compares exactly, so two identical infinite radii are approximately
        /// equal and this stays reflexive for every sphere.
        /// </returns>
        public bool ApproximatelyEquals(Sphere other, float tolerance)
        {
            if (float.IsNaN(tolerance) || float.IsInfinity(tolerance) || tolerance < 0f)
            {
                return false;
            }

            return WallMath.WithinTolerance(center, other.center, tolerance)
                && WallMath.WithinTolerance(radius, other.radius, tolerance);
        }

        /// <summary>
        /// Determines whether this sphere equals another object. Only another <see cref="Sphere"/>
        /// can be equal to a sphere.
        /// </summary>
        /// <param name="obj">The object to compare.</param>
        /// <returns>True if the object is a Sphere with exactly the same center and radius.</returns>
        public override bool Equals(object obj)
        {
            return obj is Sphere other && Equals(other);
        }

        /// <summary>
        /// Gets the hash code for this sphere, derived from exactly the members
        /// <see cref="Equals(Sphere)"/> compares.
        /// </summary>
        /// <returns>A hash code for the current sphere.</returns>
        public override int GetHashCode()
        {
            return Objects.HashCode(center, radius);
        }

        /// <summary>
        /// Determines whether two spheres are equal.
        /// </summary>
        public static bool operator ==(Sphere left, Sphere right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether two spheres are not equal.
        /// </summary>
        public static bool operator !=(Sphere left, Sphere right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Returns a string representation of this sphere.
        /// </summary>
        /// <returns>A string describing the sphere's center and radius.</returns>
        public override string ToString()
        {
            return $"Sphere(center: {center}, radius: {radius})";
        }
    }
}
