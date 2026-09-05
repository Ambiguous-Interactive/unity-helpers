// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System.Collections.Generic;
    using DataStructure;
    using DataStructure.Adapters;
    using UnityEngine;

    /// <summary>
    /// Provides extension methods for Circle data structure operations, particularly grid enumeration.
    /// </summary>
    /// <remarks>
    /// Thread Safety: All methods are thread-safe as they operate on value types without shared state.
    /// Performance: EnumerateArea methods use bounding box optimization and avoid allocations where possible.
    /// </remarks>
    public static class CircleExtensions
    {
        /// <summary>
        /// Enumerates all integer grid points within the circle's area using lazy evaluation.
        /// </summary>
        /// <param name="circle">The circle whose area to enumerate.</param>
        /// <param name="z">The z-coordinate to assign to all enumerated points (default: 0).</param>
        /// <returns>
        /// A lazy-evaluated enumerable of FastVector3Int points where each point's (x,y) distance from circle center
        /// is less than or equal to circle radius, with the specified z coordinate.
        /// </returns>
        /// <remarks>
        /// Null Handling: N/A - operates on value types.
        /// Thread Safety: Thread-safe - no shared state, operates on value type inputs.
        /// Performance: O(w*h) where w,h are bounding box dimensions. Uses lazy evaluation (yield return).
        /// Bounding box optimization reduces iterations from all grid points to only those in circle's AABB.
        /// Allocations: Minimal - uses yield return for lazy evaluation. Each returned FastVector3Int is a value type.
        /// No intermediate collections allocated.
        /// Edge Cases: Zero radius returns only center point (if center is on integer coordinates).
        /// Negative radius returns no points. Uses squared distance comparison to avoid sqrt.
        /// Points exactly on the boundary (distance == radius) are included.
        /// </remarks>
        public static IEnumerable<FastVector3Int> EnumerateArea(this Circle circle, int z = 0)
        {
            int radiusCeil = Mathf.CeilToInt(circle.radius);
            int minX = Mathf.FloorToInt(circle.center.x - circle.radius);
            int maxX = Mathf.CeilToInt(circle.center.x + circle.radius);
            int minY = Mathf.FloorToInt(circle.center.y - circle.radius);
            int maxY = Mathf.CeilToInt(circle.center.y + circle.radius);

            float radiusSquared = circle.radius * circle.radius;
            Vector2 center = circle.center;

            for (int x = minX; x <= maxX; ++x)
            {
                for (int y = minY; y <= maxY; ++y)
                {
                    float dx = x - center.x;
                    float dy = y - center.y;
                    float distanceSquared = dx * dx + dy * dy;

                    if (distanceSquared <= radiusSquared)
                    {
                        yield return new FastVector3Int(x, y, z);
                    }
                }
            }
        }

        /// <summary>
        /// Enumerates all integer grid points within the circle's area into a pre-existing buffer list.
        /// This is a non-lazy, eager evaluation version that populates a collection immediately.
        /// </summary>
        /// <param name="circle">The circle whose area to enumerate.</param>
        /// <param name="buffer">The list to populate with points. Will be cleared before populating.</param>
        /// <param name="z">The z-coordinate to assign to all enumerated points (default: 0).</param>
        /// <returns>The same buffer list passed in, now containing all points within the circle.</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if buffer is null.
        /// Thread Safety: Thread-safe if buffer is not accessed by other threads during execution.
        /// Performance: O(w*h) where w,h are bounding box dimensions. Eagerly evaluates all points.
        /// Bounding box optimization reduces iterations. Same algorithm as lazy version but immediate execution.
        /// Allocations: No allocations except potential buffer growth if capacity is insufficient.
        /// Buffer is cleared first, then populated. Consider pre-sizing buffer if point count is known.
        /// Edge Cases: Zero radius adds only center point (if on integer coordinates).
        /// Negative radius clears buffer and returns empty. Uses squared distance to avoid sqrt.
        /// Points exactly on boundary (distance == radius) are included.
        /// Buffer capacity may grow if circle contains more points than current capacity.
        /// </remarks>
        public static List<FastVector3Int> EnumerateArea(
            this Circle circle,
            List<FastVector3Int> buffer,
            int z = 0
        )
        {
            buffer.Clear();

            int radiusCeil = Mathf.CeilToInt(circle.radius);
            int minX = Mathf.FloorToInt(circle.center.x - circle.radius);
            int maxX = Mathf.CeilToInt(circle.center.x + circle.radius);
            int minY = Mathf.FloorToInt(circle.center.y - circle.radius);
            int maxY = Mathf.CeilToInt(circle.center.y + circle.radius);

            float radiusSquared = circle.radius * circle.radius;
            Vector2 center = circle.center;

            for (int x = minX; x <= maxX; ++x)
            {
                for (int y = minY; y <= maxY; ++y)
                {
                    float dx = x - center.x;
                    float dy = y - center.y;
                    float distanceSquared = dx * dx + dy * dy;

                    if (distanceSquared <= radiusSquared)
                    {
                        buffer.Add(new FastVector3Int(x, y, z));
                    }
                }
            }

            return buffer;
        }
    }
}
