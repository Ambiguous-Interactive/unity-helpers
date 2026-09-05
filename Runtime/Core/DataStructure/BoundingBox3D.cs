// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Text.Json.Serialization;
    using Helper;
    using Math;
    using UnityEngine;

    /// <summary>
    /// Axis-aligned bounding box that uses half-open semantics on its upper bounds so hit tests behave consistently with grid and voxel data.
    /// Useful for spatial queries, containment checks, and broad-phase culling without off-by-one float errors.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// BoundingBox3D box = BoundingBox3D.FromCenterAndSize(Vector3.zero, Vector3.one * 10f);
    /// bool containsPlayer = box.Contains(playerPosition);
    /// BoundingBox3D expanded = box.ExpandToInclude(enemyPosition);
    /// ]]></code>
    /// </example>
    public readonly struct BoundingBox3D : IEquatable<BoundingBox3D>
    {
        private const float MinimumExclusivePadding = 1e-6f;

        public readonly Vector3 min;
        public readonly Vector3 max;

        [JsonConstructor]
        public BoundingBox3D(Vector3 min, Vector3 max)
        {
            if (max.x < min.x || max.y < min.y || max.z < min.z)
            {
                throw new ArgumentException("Min must be less than or equal to max on all axes.");
            }

            this.min = min;

            if (min.x == max.x || min.y == max.y || min.z == max.z)
            {
                this.max = EnsureExclusiveMax(min, max);
            }
            else
            {
                this.max = max;
            }
        }

        public Vector3 Center => (min + max) * 0.5f;

        public Vector3 Size => max - min;

        public float Volume
        {
            get
            {
                Vector3 size = Size;
                return size.x * size.y * size.z;
            }
        }

        public bool IsEmpty => max.x <= min.x || max.y <= min.y || max.z <= min.z;

        public static BoundingBox3D Empty => default;

        public static BoundingBox3D FromCenterAndSize(Vector3 center, Vector3 size)
        {
            Vector3 half = size * 0.5f;
            Vector3 min = center - half;
            Vector3 max = center + half;
            return new BoundingBox3D(min, max);
        }

        public static BoundingBox3D FromClosedBounds(Bounds bounds)
        {
            return new BoundingBox3D(bounds.min, bounds.max);
        }

        /// <summary>
        /// Creates a BoundingBox3D from a Unity Bounds treating the Bounds' max as inclusive
        /// by converting the closed interval [min, max] to a half-open interval [min, max)
        /// with an exclusive max that is the next representable float past the provided max.
        /// This makes point-in-box tests consistent with Unity's Bounds.Contains semantics.
        /// </summary>
        public static BoundingBox3D FromClosedBoundsInclusiveMax(Bounds bounds)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            // Convert Unity's closed max to half-open by nudging max strictly past the provided value
            Vector3 exclusiveMax = new(NextFloat(max.x), NextFloat(max.y), NextFloat(max.z));
            return new BoundingBox3D(min, exclusiveMax);
        }

        public static BoundingBox3D FromPoint(Vector3 point)
        {
            Vector3 exclusiveMax = new(NextFloat(point.x), NextFloat(point.y), NextFloat(point.z));
            return new BoundingBox3D(point, exclusiveMax);
        }

        public BoundingBox3D ExpandToInclude(Vector3 point)
        {
            Vector3 localMin = min;
            if (point.x < localMin.x)
            {
                localMin.x = point.x;
            }
            if (point.y < localMin.y)
            {
                localMin.y = point.y;
            }
            if (point.z < localMin.z)
            {
                localMin.z = point.z;
            }

            Vector3 localMax = max;
            if (localMax.x <= point.x)
            {
                localMax.x = NextFloat(point.x);
            }
            if (localMax.y <= point.y)
            {
                localMax.y = NextFloat(point.y);
            }
            if (localMax.z <= point.z)
            {
                localMax.z = NextFloat(point.z);
            }

            return new BoundingBox3D(localMin, localMax);
        }

        public BoundingBox3D ExpandToInclude(BoundingBox3D other)
        {
            if (other.IsEmpty)
            {
                return this;
            }

            if (IsEmpty)
            {
                return other;
            }

            Vector3 localMin = new(
                Math.Min(min.x, other.min.x),
                Math.Min(min.y, other.min.y),
                Math.Min(min.z, other.min.z)
            );

            Vector3 localMax = new(
                Math.Max(max.x, other.max.x),
                Math.Max(max.y, other.max.y),
                Math.Max(max.z, other.max.z)
            );

            return new BoundingBox3D(localMin, localMax);
        }

        public BoundingBox3D Encapsulate(Vector3 point) => ExpandToInclude(point);

        public BoundingBox3D Encapsulate(BoundingBox3D other) => ExpandToInclude(other);

        public BoundingBox3D Union(BoundingBox3D other) => ExpandToInclude(other);

        public BoundingBox3D EnsureMinimumSize(float minimum)
        {
            if (minimum <= 0f || IsEmpty)
            {
                return this;
            }

            Vector3 size = Size;
            Vector3 localMin = min;
            Vector3 localMax = max;
            bool changed = false;

            if (size.x < minimum)
            {
                float delta = (minimum - size.x) * 0.5f;
                localMin.x -= delta;
                localMax.x += delta;
                changed = true;
            }

            if (size.y < minimum)
            {
                float delta = (minimum - size.y) * 0.5f;
                localMin.y -= delta;
                localMax.y += delta;
                changed = true;
            }

            if (size.z < minimum)
            {
                float delta = (minimum - size.z) * 0.5f;
                localMin.z -= delta;
                localMax.z += delta;
                changed = true;
            }

            return changed ? new BoundingBox3D(localMin, localMax) : this;
        }

        public bool Contains(Vector3 point)
        {
            return min.x <= point.x
                && min.y <= point.y
                && min.z <= point.z
                && point.x < max.x
                && point.y < max.y
                && point.z < max.z;
        }

        public bool Contains(BoundingBox3D other)
        {
            // Empty boxes are not contained by anything, not even themselves
            if (other.IsEmpty)
            {
                return false;
            }

            return min.x <= other.min.x
                && min.y <= other.min.y
                && min.z <= other.min.z
                && other.max.x <= max.x
                && other.max.y <= max.y
                && other.max.z <= max.z;
        }

        public BoundingBox3D? Intersection(BoundingBox3D other)
        {
            if (!Intersects(other))
            {
                return null;
            }

            Vector3 intersectionMin = new(
                Math.Max(min.x, other.min.x),
                Math.Max(min.y, other.min.y),
                Math.Max(min.z, other.min.z)
            );

            Vector3 intersectionMax = new(
                Math.Min(max.x, other.max.x),
                Math.Min(max.y, other.max.y),
                Math.Min(max.z, other.max.z)
            );

            return new BoundingBox3D(intersectionMin, intersectionMax);
        }

        public bool Intersects(BoundingBox3D other)
        {
            return min.x < other.max.x
                && other.min.x < max.x
                && min.y < other.max.y
                && other.min.y < max.y
                && min.z < other.max.z
                && other.min.z < max.z;
        }

        /// <summary>
        /// Determines whether this bounding box intersects with a line segment.
        /// Returns true if the line segment intersects the bounding box.
        /// </summary>
        /// <param name="line">The line segment to test for intersection.</param>
        /// <returns>True if the line segment intersects the bounding box.</returns>
        public bool Intersects(Line3D line)
        {
            return line.Intersects(this);
        }

        /// <summary>
        /// Calculates the shortest distance from this bounding box to a line segment.
        /// Returns 0 if the line intersects the bounding box.
        /// </summary>
        /// <param name="line">The line segment to measure distance from.</param>
        /// <returns>The shortest distance from the bounding box to the line segment.</returns>
        public float DistanceToLine(Line3D line)
        {
            return line.DistanceToBounds(this);
        }

        /// <summary>
        /// Finds the closest point on a line segment to this bounding box.
        /// </summary>
        /// <param name="line">The line segment.</param>
        /// <returns>The closest point on the line segment to this bounding box.</returns>
        public Vector3 ClosestPointOnLine(Line3D line)
        {
            return line.ClosestPointOnBounds(this);
        }

        public Vector3 ClosestPoint(Vector3 point)
        {
            // Closest-point queries clamp to the representable boundary despite half-open containment.
            return new Vector3(
                Mathf.Clamp(point.x, min.x, max.x),
                Mathf.Clamp(point.y, min.y, max.y),
                Mathf.Clamp(point.z, min.z, max.z)
            );
        }

        public void GetCorners(Vector3[] corners)
        {
            if (corners == null || corners.Length < 8)
            {
                throw new ArgumentException(
                    "Corners array must not be null and have at least 8 elements."
                );
            }

            corners[0] = new Vector3(min.x, min.y, min.z);
            corners[1] = new Vector3(max.x, min.y, min.z);
            corners[2] = new Vector3(min.x, max.y, min.z);
            corners[3] = new Vector3(max.x, max.y, min.z);
            corners[4] = new Vector3(min.x, min.y, max.z);
            corners[5] = new Vector3(max.x, min.y, max.z);
            corners[6] = new Vector3(min.x, max.y, max.z);
            corners[7] = new Vector3(max.x, max.y, max.z);
        }

        public float DistanceSquaredTo(Vector3 point)
        {
            Vector3 closest = ClosestPoint(point);
            return (closest - point).sqrMagnitude;
        }

        public Bounds ToBounds()
        {
            Vector3 size = Size;
            return new Bounds(Center, size);
        }

        public bool Equals(BoundingBox3D other)
        {
            return min.Equals(other.min) && max.Equals(other.max);
        }

        public override bool Equals(object obj)
        {
            return obj is BoundingBox3D other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Objects.HashCode(min, max);
        }

        public static bool operator ==(BoundingBox3D left, BoundingBox3D right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(BoundingBox3D left, BoundingBox3D right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return $"BoundingBox3D(min: {min}, max: {max})";
        }

        private static Vector3 EnsureExclusiveMax(Vector3 min, Vector3 max)
        {
            Vector3 exclusive = max;
            if (exclusive.x <= min.x)
            {
                exclusive.x = NextFloat(min.x + MinimumExclusivePadding);
            }
            if (exclusive.y <= min.y)
            {
                exclusive.y = NextFloat(min.y + MinimumExclusivePadding);
            }
            if (exclusive.z <= min.z)
            {
                exclusive.z = NextFloat(min.z + MinimumExclusivePadding);
            }
            return exclusive;
        }

        private static float NextFloat(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return value;
            }

            if (value == float.MaxValue)
            {
                return value;
            }

            if (value == float.MinValue)
            {
                return BitConverter.Int32BitsToSingle(unchecked((int)0xFF7FFFFF));
            }

            if (value == 0f)
            {
                return float.Epsilon;
            }

            int bits = BitConverter.SingleToInt32Bits(value);
            bits = 0f < value ? bits + 1 : bits - 1;
            return BitConverter.Int32BitsToSingle(bits);
        }
    }
}
