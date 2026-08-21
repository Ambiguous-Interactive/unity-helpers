// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;

    /// <summary>
    /// Grid traversal and BoundsInt utility helpers that operate on discretized space.
    /// </summary>
    public static partial class UnityExtensions
    {
        /// <summary>
        /// Creates a new BoundsInt with additional padding in the X and Y directions.
        /// </summary>
        /// <param name="bounds">The source bounds to add padding to.</param>
        /// <param name="xPadding">The padding to add to both left and right sides.</param>
        /// <param name="yPadding">The padding to add to both top and bottom sides.</param>
        /// <returns>A new BoundsInt expanded by the specified padding amounts.</returns>
        /// <remarks>
        /// Thread Safety: Thread-safe, no Unity API calls.
        /// Null Handling: Not applicable for value types.
        /// Performance: O(1) - Simple arithmetic.
        /// Allocations: None - returns value type.
        /// Unity Behavior: Z dimension remains unchanged.
        /// Edge Cases: Negative padding shrinks the bounds. Size increases by 2*padding in each direction.
        /// </remarks>
        public static BoundsInt WithPadding(this in BoundsInt bounds, int xPadding, int yPadding)
        {
            BoundsInt self = bounds;
            Vector3Int size = self.size;
            return new BoundsInt(
                self.xMin - xPadding,
                self.yMin - yPadding,
                self.zMin,
                size.x + 2 * xPadding,
                size.y + 2 * yPadding,
                size.z
            );
        }

        /// <summary>
        /// Enumerates all FastVector3Int positions within the bounds.
        /// </summary>
        /// <param name="bounds">The bounds to enumerate positions within.</param>
        /// <returns>An enumerable of all FastVector3Int positions within the bounds.</returns>
        /// <remarks>
        /// Thread Safety: Thread-safe, no Unity API calls.
        /// Null Handling: Not applicable for value types.
        /// Performance: O(volume) where volume is the number of cells in the bounds.
        /// Allocations: Uses yield return, allocates enumerator. Each FastVector3Int is a value type.
        /// Unity Behavior: Uses half-open interval [min, max) consistent with BoundsInt.
        /// Edge Cases: Zero or negative size bounds yield no positions.
        /// Iteration order is X (innermost), then Y, then Z (outermost).
        /// </remarks>
        public static IEnumerable<FastVector3Int> AllFastPositionsWithin(this BoundsInt bounds)
        {
            Vector3Int min = bounds.min;
            Vector3Int max = bounds.max;
            for (int x = min.x; x < max.x; ++x)
            {
                for (int y = min.y; y < max.y; ++y)
                {
                    for (int z = min.z; z < max.z; ++z)
                    {
                        yield return new FastVector3Int(x, y, z);
                    }
                }
            }
        }

        /// <summary>
        /// Fills a list with all FastVector3Int positions within the bounds.
        /// </summary>
        /// <param name="bounds">The bounds to get positions from.</param>
        /// <param name="buffer">The list to clear and fill with positions.</param>
        /// <returns>The buffer list containing all positions.</returns>
        /// <remarks>
        /// Thread Safety: Thread-safe if buffer is not accessed concurrently.
        /// Null Handling: Throws NullReferenceException if buffer is null.
        /// Performance: O(volume) where volume is the number of cells in the bounds.
        /// Allocations: May allocate if buffer capacity is insufficient. Clears buffer first.
        /// Unity Behavior: Uses half-open interval [min, max) consistent with BoundsInt.
        /// Edge Cases: Zero or negative size bounds result in an empty buffer.
        /// Iteration order is X (innermost), then Y, then Z (outermost).
        /// </remarks>
        public static List<FastVector3Int> AllFastPositionsWithin(
            this in BoundsInt bounds,
            List<FastVector3Int> buffer
        )
        {
            buffer.Clear();
            BoundsInt self = bounds;
            Vector3Int min = self.min;
            Vector3Int max = self.max;
            for (int x = min.x; x < max.x; ++x)
            {
                for (int y = min.y; y < max.y; ++y)
                {
                    for (int z = min.z; z < max.z; ++z)
                    {
                        FastVector3Int position = new(x, y, z);
                        buffer.Add(position);
                    }
                }
            }

            return buffer;
        }

        /// <summary>
        /// Determines if a BoundsInt contains a FastVector3Int position.
        /// </summary>
        /// <param name="bounds">The bounds to test containment in.</param>
        /// <param name="position">The position to test.</param>
        /// <returns>True if the position is within the bounds; otherwise, false.</returns>
        /// <remarks>
        /// Thread Safety: Thread-safe, no Unity API calls.
        /// Null Handling: Not applicable for value types.
        /// Performance: O(1) - Delegates to BoundsInt.Contains via implicit conversion.
        /// Allocations: None.
        /// Unity Behavior: Uses half-open interval [min, max) for containment test.
        /// Edge Cases: Points on the max boundary are NOT contained.
        /// </remarks>
        public static bool Contains(this in BoundsInt bounds, FastVector3Int position)
        {
            BoundsInt self = bounds;
            return self.Contains(position);
        }

        /// <summary>
        /// Determines if a FastVector3Int position is on the 2D edge of a BoundsInt (ignores Z axis).
        /// </summary>
        /// <param name="position">The position to test.</param>
        /// <param name="bounds">The bounds to test against.</param>
        /// <returns>True if the position is on the 2D boundary of the bounds; otherwise, false.</returns>
        /// <remarks>
        /// Thread Safety: Thread-safe, no Unity API calls.
        /// Null Handling: Not applicable for value types.
        /// Performance: O(1) - Simple comparisons.
        /// Allocations: None.
        /// Unity Behavior: Tests if position is on the min or max-1 boundary in X or Y.
        /// Edge Cases: Position must be within bounds AND on an edge to return true.
        /// Uses max-1 because BoundsInt uses half-open intervals. Z axis is ignored.
        /// </remarks>
        public static bool IsOnEdge2D(this FastVector3Int position, BoundsInt bounds)
        {
            if (bounds.xMin == position.x || bounds.xMax - 1 == position.x)
            {
                return bounds.yMin <= position.y && position.y < bounds.yMax;
            }

            if (bounds.yMin == position.y || bounds.yMax - 1 == position.y)
            {
                return bounds.xMin <= position.x && position.x < bounds.xMax;
            }

            return false;
        }
    }
}
