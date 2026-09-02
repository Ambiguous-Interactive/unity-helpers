// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    public static partial class UnityExtensions
    {
        /*
            Every `Bounds` / `BoundsInt` receiver below is taken by `in`, and every body that reads
            more than one member of it opens with a single explicit local copy.

            Both halves are load-bearing. By-value receivers copy 24 bytes at EVERY call site, which
            `ErrorProne.NET.Structs` reports as `EPS06` in a consumer's build -- a warning the
            consumer cannot fix without abandoning extension syntax (#512). `in` removes it there.
            But Unity's `Bounds` is not a `readonly struct` and exposes no fields, so each property
            read through an `in` parameter takes its OWN defensive copy: measured on the shipped
            analyzer, marking `FastIntersects2D` `in` alone removed 18 call-site copies and added 4
            defensive ones inside. One local copy is the minimum a property-only struct can be read
            through, so these bodies pay exactly what by-value paid, at one site instead of N.
        */
        public static bool FastIntersects(this in Bounds bounds, Bounds other)
        {
            Bounds self = bounds;
            // Degenerate bounds (zero volume) do not intersect
            Vector3 sizeA = self.size;
            Vector3 sizeB = other.size;
            if (
                sizeA.x <= 0f
                || sizeA.y <= 0f
                || sizeA.z <= 0f
                || sizeB.x <= 0f
                || sizeB.y <= 0f
                || sizeB.z <= 0f
            )
            {
                return false;
            }
            Vector3 boundsMin = self.min;
            Vector3 otherMax = other.max;
            if (otherMax.x < boundsMin.x || otherMax.y < boundsMin.y || otherMax.z < boundsMin.z)
            {
                return false;
            }

            Vector3 boundsMax = self.max;
            Vector3 otherMin = other.min;
            return otherMin.x <= boundsMax.x
                && otherMin.y <= boundsMax.y
                && otherMin.z <= boundsMax.z;
        }

        /// <summary>
        /// Fast 2D containment test for BoundsInt and FastVector3Int (ignores Z axis).
        /// </summary>
        /// <param name="bounds">The bounds to test containment in.</param>
        /// <param name="position">The position to test.</param>
        /// <returns>True if the position is inside the 2D bounds (XY plane only); otherwise, false.</returns>
        /// <remarks>
        /// Thread Safety: Thread-safe, no Unity API calls.
        /// Null Handling: Not applicable for value types.
        /// Performance: O(1) - Four comparisons.
        /// Allocations: None.
        /// Unity Behavior: Uses half-open interval [min, max) for containment test.
        /// Edge Cases: Point on max boundary is NOT contained. Z coordinate is ignored.
        /// </remarks>
        public static bool FastContains2D(this in BoundsInt bounds, FastVector3Int position)
        {
            BoundsInt self = bounds;
            return self.xMin <= position.x
                && self.yMin <= position.y
                && position.x < self.xMax
                && position.y < self.yMax;
        }

        /// <summary>
        /// Fast 2D intersection test for BoundsInt (ignores Z axis).
        /// </summary>
        /// <param name="bounds">The first bounds.</param>
        /// <param name="other">The second bounds to test intersection with.</param>
        /// <returns>True if the 2D bounds intersect (XY plane only); otherwise, false.</returns>
        /// <remarks>
        /// Thread Safety: Thread-safe, no Unity API calls.
        /// Null Handling: Not applicable for value types.
        /// Performance: O(1) - Optimized comparisons.
        /// Allocations: None.
        /// Unity Behavior: Uses BoundsInt min/max properties.
        /// Edge Cases: Zero-size bounds (size <= 0 in X or Y) cannot intersect and return false.
        /// Bounds that touch at an edge are considered intersecting (inclusive). Z axis is ignored.
        /// </remarks>
        public static bool FastIntersects2D(this in BoundsInt bounds, BoundsInt other)
        {
            BoundsInt self = bounds;
            Vector3Int selfSize = self.size;
            Vector3Int otherSize = other.size;
            // Zero-size bounds cannot intersect
            if (selfSize.x <= 0 || selfSize.y <= 0 || otherSize.x <= 0 || otherSize.y <= 0)
            {
                return false;
            }

            if (other.xMax < self.xMin || other.yMax < self.yMin)
            {
                return false;
            }

            return other.xMin <= self.xMax && other.yMin <= self.yMax;
        }

        /// <summary>
        /// Fast 2D containment test for Bounds and Vector2 (ignores Z axis).
        /// </summary>
        /// <param name="bounds">The bounds to test containment in.</param>
        /// <param name="position">The 2D position to test.</param>
        /// <returns>True if the position is inside the 2D bounds (XY plane only); otherwise, false.</returns>
        /// <remarks>
        /// Thread Safety: Thread-safe, no Unity API calls beyond property access.
        /// Null Handling: Not applicable for value types.
        /// Performance: O(1) - Optimized to cache min/max values.
        /// Allocations: None.
        /// Unity Behavior: Uses closed interval [min, max] for containment test (unlike BoundsInt).
        /// Edge Cases: Points on the boundary ARE contained. Z coordinate is ignored.
        /// </remarks>
        public static bool FastContains2D(this in Bounds bounds, Vector2 position)
        {
            Bounds self = bounds;
            Vector3 min = self.min;
            if (position.x < min.x || position.y < min.y)
            {
                return false;
            }
            Vector3 max = self.max;
            return position.x <= max.x && position.y <= max.y;
        }

        /// <summary>
        /// Fast 2D containment test to check if one Bounds contains another (ignores Z axis).
        /// </summary>
        /// <param name="bounds">The outer bounds.</param>
        /// <param name="other">The inner bounds to test if contained.</param>
        /// <returns>True if other is completely inside bounds (XY plane only); otherwise, false.</returns>
        /// <remarks>
        /// Thread Safety: Thread-safe, no Unity API calls beyond property access.
        /// Null Handling: Not applicable for value types.
        /// Performance: O(1) - Optimized to cache min/max values.
        /// Allocations: None.
        /// Unity Behavior: Uses Bounds min/max properties.
        /// Edge Cases: If other touches the boundary of bounds, it's still considered contained.
        /// Z axis is ignored.
        /// </remarks>
        public static bool FastContains2D(this in Bounds bounds, Bounds other)
        {
            Bounds self = bounds;
            Vector3 boundsMin = self.min;
            Vector3 otherMin = other.min;
            if (otherMin.x < boundsMin.x || otherMin.y < boundsMin.y)
            {
                return false;
            }

            Vector3 boundsMax = self.max;
            Vector3 otherMax = other.max;
            return otherMax.x <= boundsMax.x && otherMax.y <= boundsMax.y;
        }

        /// <summary>
        /// Fast 2D intersection test for Bounds (ignores Z axis).
        /// </summary>
        /// <param name="bounds">The first bounds.</param>
        /// <param name="other">The second bounds to test intersection with.</param>
        /// <returns>True if the 2D bounds intersect (XY plane only); otherwise, false.</returns>
        /// <remarks>
        /// Thread Safety: Thread-safe, no Unity API calls beyond property access.
        /// Null Handling: Not applicable for value types.
        /// Performance: O(1) - Optimized to cache min/max values.
        /// Allocations: None.
        /// Unity Behavior: Uses Bounds min/max properties.
        /// Edge Cases: Bounds that touch at edges are considered intersecting (inclusive). Z axis is ignored.
        /// </remarks>
        public static bool FastIntersects2D(this in Bounds bounds, Bounds other)
        {
            Bounds self = bounds;
            Vector3 boundsMin = self.min;
            Vector3 otherMax = other.max;
            if (otherMax.x < boundsMin.x || otherMax.y < boundsMin.y)
            {
                return false;
            }

            Vector3 boundsMax = self.max;
            Vector3 otherMin = other.min;
            return otherMin.x <= boundsMax.x && otherMin.y <= boundsMax.y;
        }

        /// <summary>
        /// Fast 2D overlap test for Bounds (ignores Z axis). Functionally identical to FastIntersects2D.
        /// </summary>
        /// <param name="bounds">The first bounds.</param>
        /// <param name="other">The second bounds to test overlap with.</param>
        /// <returns>True if the 2D bounds overlap (XY plane only); otherwise, false.</returns>
        /// <remarks>
        /// Thread Safety: Thread-safe, no Unity API calls beyond property access.
        /// Null Handling: Not applicable for value types.
        /// Performance: O(1) - Optimized to cache min/max values.
        /// Allocations: None.
        /// Unity Behavior: Uses Bounds min/max properties. Identical to FastIntersects2D.
        /// Edge Cases: Bounds that touch but don't overlap return false. Z axis is ignored.
        /// </remarks>
        public static bool Overlaps2D(this in Bounds bounds, Bounds other)
        {
            Bounds self = bounds;
            Vector3 boundsMin = self.min;
            Vector3 otherMax = other.max;
            if (otherMax.x < boundsMin.x || otherMax.y < boundsMin.y)
            {
                return false;
            }

            Vector3 boundsMax = self.max;
            Vector3 otherMin = other.min;
            return otherMin.x <= boundsMax.x && otherMin.y <= boundsMax.y;
        }

        /*
            =========================
            3D Bounds helpers (opt-in tolerance)
            =========================
        */

        /// <summary>
        /// Fast 3D point containment with optional tolerance and half-open semantics [min, max).
        /// A point on the max face is NOT contained.
        /// </summary>
        public static bool FastContains3D(this in Bounds bounds, Vector3 p, float tolerance = 0f)
        {
            Bounds self = bounds;
            Vector3 min = self.min;
            Vector3 max = self.max;
            return min.x - tolerance <= p.x
                && p.x < max.x + tolerance
                && min.y - tolerance <= p.y
                && p.y < max.y + tolerance
                && min.z - tolerance <= p.z
                && p.z < max.z + tolerance;
        }

        /// <summary>
        /// Fast 3D containment test (box in box) with optional tolerance and inclusive semantics on max faces.
        /// Returns true if 'other' is fully inside or touching 'bounds' (with tolerance).
        /// </summary>
        public static bool FastContains3D(this in Bounds bounds, Bounds other, float tolerance = 0f)
        {
            Bounds self = bounds;
            Vector3 min = self.min;
            Vector3 max = self.max;
            Vector3 omin = other.min;
            Vector3 omax = other.max;
            if (
                omin.x < min.x - tolerance
                || omin.y < min.y - tolerance
                || omin.z < min.z - tolerance
            )
            {
                return false;
            }
            return omax.x <= max.x + tolerance
                && omax.y <= max.y + tolerance
                && omax.z <= max.z + tolerance;
        }

        /*
            =========================
            3D Bounds helpers (opt-in tolerance)
            =========================
        */

        /// <summary>
        /// Fast 3D bounds intersection with optional tolerance.
        /// Touching at faces is considered intersection (inclusive at boundaries).
        /// </summary>
        public static bool FastIntersects3D(this in Bounds a, Bounds b, float tolerance = 0f)
        {
            Bounds self = a;
            // Degenerate bounds (zero volume) do not intersect
            Vector3 asize = self.size;
            Vector3 bsize = b.size;
            if (
                asize.x <= 0f
                || asize.y <= 0f
                || asize.z <= 0f
                || bsize.x <= 0f
                || bsize.y <= 0f
                || bsize.z <= 0f
            )
            {
                return false;
            }
            Vector3 amin = self.min;
            Vector3 bmax = b.max;
            if (
                bmax.x < amin.x - tolerance
                || bmax.y < amin.y - tolerance
                || bmax.z < amin.z - tolerance
            )
            {
                return false;
            }

            Vector3 amax = self.max;
            Vector3 bmin = b.min;
            return bmin.x <= amax.x + tolerance
                && bmin.y <= amax.y + tolerance
                && bmin.z <= amax.z + tolerance;
        }

        /// <summary>
        /// Fast 3D containment test (box in box) with optional tolerance and half-open semantics on max faces.
        /// Returns true only if 'other' is fully inside 'bounds' and does NOT touch the max faces.
        /// Equivalent to: other.min >= bounds.min and other.max < bounds.max (with tolerance).
        /// </summary>
        public static bool FastContainsHalfOpen3D(
            this in Bounds bounds,
            Bounds other,
            float tolerance = 0f
        )
        {
            Bounds self = bounds;
            Vector3 min = self.min;
            Vector3 max = self.max;
            Vector3 omin = other.min;
            Vector3 omax = other.max;
            if (
                omin.x < min.x - tolerance
                || omin.y < min.y - tolerance
                || omin.z < min.z - tolerance
            )
            {
                return false;
            }
            return omax.x < max.x - tolerance
                && omax.y < max.y - tolerance
                && omax.z < max.z - tolerance;
        }
    }
}
