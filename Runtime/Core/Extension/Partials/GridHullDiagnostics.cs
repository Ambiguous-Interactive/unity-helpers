// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using DataStructure.Adapters;
    using Helper;
    using UnityEngine;

    public static partial class UnityExtensions
    {
        public static bool IsConvexHullInsideConvexHull(
            this List<Vector2> convexHull,
            List<Vector2> maybeInside
        )
        {
            int orientation = DetermineConvexHullOrientation(convexHull);
            foreach (Vector2 point in maybeInside)
            {
                if (!IsPointInsideConvexHull(convexHull, point, orientation))
                {
                    return false;
                }
            }
            return true;
        }

        private static int DetermineConvexHullOrientation(List<Vector2> convexHull)
        {
            if (convexHull == null || convexHull.Count < 3)
            {
                return 0;
            }
            double area = 0d;
            for (int i = 0; i < convexHull.Count; ++i)
            {
                Vector2 a = convexHull[i];
                Vector2 b = convexHull[(i + 1) % convexHull.Count];
                area += (double)a.x * b.y - (double)a.y * b.x;
            }
            if (Math.Abs(area) <= ConvexHullOrientationEpsilon)
            {
                return 0;
            }
            return 0d < area ? 1 : -1;
        }

        private static bool IsPointInsideConvexHull(
            List<Vector2> convexHull,
            Vector2 point,
            int expectedSide
        )
        {
            if (convexHull == null || convexHull.Count == 0)
            {
                return true;
            }
            int requiredSide = expectedSide;
            for (int i = 0; i < convexHull.Count; ++i)
            {
                Vector2 lhs = convexHull[i];
                Vector2 rhs = convexHull[(i + 1) % convexHull.Count];
                float relation = Geometry.IsAPointLeftOfVectorOrOnTheLine(lhs, rhs, point);
                if (Mathf.Abs(relation) <= ConvexHullRelationEpsilon)
                {
                    continue;
                }
                int side = requiredSide;
                if (side == 0)
                {
                    side = 0f < relation ? 1 : -1;
                    requiredSide = side;
                }
                if (relation * side < -ConvexHullRelationEpsilon)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Determines if one convex hull is completely inside another convex hull.
        /// </summary>
        /// <param name="convexHull">The outer convex hull to test against.</param>
        /// <param name="grid">The Grid used for coordinate conversion.</param>
        /// <param name="maybeInside">The convex hull to test if it's inside.</param>
        /// <returns>True if all points of maybeInside are inside convexHull; otherwise, false.</returns>
        /// <remarks>
        /// Thread Safety: Must be called from Unity main thread (uses Grid).
        /// Null Handling: Returns true if convexHull is null or empty. Throws if grid or maybeInside is null.
        /// Performance: O(nm) where n is outer hull size and m is inner hull size.
        /// Allocations: None.
        /// Unity Behavior: Uses grid.CellToWorld for spatial calculations.
        /// Edge Cases: Empty or null outer hull returns true. Determines hull orientation automatically.
        /// </remarks>
        public static bool IsConvexHullInsideConvexHull(
            this List<FastVector3Int> convexHull,
            Grid grid,
            List<FastVector3Int> maybeInside
        )
        {
            int orientation = DetermineConvexHullOrientation(convexHull, grid);
            foreach (FastVector3Int point in maybeInside)
            {
                if (!IsPointInsideConvexHull(convexHull, grid, point, orientation))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Determines if a Vector3Int point is inside a convex hull.
        /// </summary>
        /// <param name="convexHull">The convex hull to test against.</param>
        /// <param name="grid">The Grid used for coordinate conversion.</param>
        /// <param name="point">The point to test.</param>
        /// <returns>True if the point is inside or on the boundary of the convex hull; otherwise, false.</returns>
        /// <remarks>
        /// Thread Safety: Must be called from Unity main thread (uses Grid).
        /// Null Handling: Returns true if convexHull is null or empty. Throws if grid is null.
        /// Performance: O(n) where n is the hull size.
        /// Allocations: None.
        /// Unity Behavior: Uses grid.CellToWorld for spatial calculations.
        /// Edge Cases: Points on the hull boundary are considered inside. Determines orientation automatically.
        /// </remarks>
        public static bool IsPointInsideConvexHull(
            this List<Vector3Int> convexHull,
            Grid grid,
            Vector3Int point
        )
        {
            int orientation = DetermineConvexHullOrientation(convexHull, grid);
            return IsPointInsideConvexHull(convexHull, grid, point, orientation);
        }

        /// <summary>
        /// Determines if a FastVector3Int point is inside a convex hull.
        /// </summary>
        /// <param name="convexHull">The convex hull to test against.</param>
        /// <param name="grid">The Grid used for coordinate conversion.</param>
        /// <param name="point">The point to test.</param>
        /// <returns>True if the point is inside or on the boundary of the convex hull; otherwise, false.</returns>
        /// <remarks>
        /// Thread Safety: Must be called from Unity main thread (uses Grid).
        /// Null Handling: Returns true if convexHull is null or empty. Throws if grid is null.
        /// Performance: O(n) where n is the hull size.
        /// Allocations: None.
        /// Unity Behavior: Uses grid.CellToWorld for spatial calculations.
        /// Edge Cases: Points on the hull boundary are considered inside. Determines orientation automatically.
        /// </remarks>
        public static bool IsPointInsideConvexHull(
            this List<FastVector3Int> convexHull,
            Grid grid,
            FastVector3Int point
        )
        {
            int orientation = DetermineConvexHullOrientation(convexHull, grid);
            return IsPointInsideConvexHull(convexHull, grid, point, orientation);
        }

        /// <summary>
        /// Determines if one convex hull is completely inside another convex hull.
        /// </summary>
        /// <param name="convexHull">The outer convex hull to test against.</param>
        /// <param name="grid">The Grid used for coordinate conversion.</param>
        /// <param name="maybeInside">The convex hull to test if it's inside.</param>
        /// <returns>True if all points of maybeInside are inside convexHull; otherwise, false.</returns>
        /// <remarks>
        /// Thread Safety: Must be called from Unity main thread (uses Grid).
        /// Null Handling: Returns true if convexHull is null or empty. Throws if grid or maybeInside is null.
        /// Performance: O(nm) where n is outer hull size and m is inner hull size.
        /// Allocations: None.
        /// Unity Behavior: Uses grid.CellToWorld for spatial calculations.
        /// Edge Cases: Empty or null outer hull returns true. Determines hull orientation automatically.
        /// </remarks>
        public static bool IsConvexHullInsideConvexHull(
            this List<Vector3Int> convexHull,
            Grid grid,
            List<Vector3Int> maybeInside
        )
        {
            int orientation = DetermineConvexHullOrientation(convexHull, grid);
            foreach (Vector3Int point in maybeInside)
            {
                if (!IsPointInsideConvexHull(convexHull, grid, point, orientation))
                {
                    return false;
                }
            }

            return true;
        }

        private static int DetermineConvexHullOrientation(
            IReadOnlyList<FastVector3Int> convexHull,
            Grid grid
        )
        {
            if (convexHull == null || convexHull.Count < 3)
            {
                return 0;
            }

            double twiceArea = 0d;
            Vector3 previousWorld = grid.CellToWorld(convexHull[^1]);
            Vector2 previous = new(previousWorld.x, previousWorld.y);
            for (int i = 0; i < convexHull.Count; ++i)
            {
                Vector3 currentWorld = grid.CellToWorld(convexHull[i]);
                Vector2 current = new(currentWorld.x, currentWorld.y);
                twiceArea += (previous.x * current.y) - (current.x * previous.y);
                previous = current;
            }

            if (Math.Abs(twiceArea) <= ConvexHullOrientationEpsilon)
            {
                return 0;
            }

            return 0d < twiceArea ? 1 : -1;
        }

        private static int DetermineConvexHullOrientation(
            IReadOnlyList<Vector3Int> convexHull,
            Grid grid
        )
        {
            if (convexHull == null || convexHull.Count < 3)
            {
                return 0;
            }

            double twiceArea = 0d;
            Vector3 previousWorld = grid.CellToWorld(convexHull[^1]);
            Vector2 previous = new(previousWorld.x, previousWorld.y);
            for (int i = 0; i < convexHull.Count; ++i)
            {
                Vector3 currentWorld = grid.CellToWorld(convexHull[i]);
                Vector2 current = new(currentWorld.x, currentWorld.y);
                twiceArea += (previous.x * current.y) - (current.x * previous.y);
                previous = current;
            }

            if (Math.Abs(twiceArea) <= ConvexHullOrientationEpsilon)
            {
                return 0;
            }

            return 0d < twiceArea ? 1 : -1;
        }

        private static bool IsPointInsideConvexHull(
            List<FastVector3Int> convexHull,
            Grid grid,
            FastVector3Int point,
            int expectedSide
        )
        {
            if (convexHull == null || convexHull.Count == 0)
            {
                return true;
            }

            int requiredSide = expectedSide;
            Vector3 pointWorld = grid.CellToWorld(point);
            for (int i = 0; i < convexHull.Count; ++i)
            {
                FastVector3Int lhs = convexHull[i];
                FastVector3Int rhs = convexHull[(i + 1) % convexHull.Count];
                float relation = Geometry.IsAPointLeftOfVectorOrOnTheLine(
                    grid.CellToWorld(lhs),
                    grid.CellToWorld(rhs),
                    pointWorld
                );

                if (Mathf.Abs(relation) <= ConvexHullRelationEpsilon)
                {
                    continue;
                }

                int side = requiredSide;
                if (side == 0)
                {
                    side = 0f < relation ? 1 : -1;
                    requiredSide = side;
                }

                if (relation * side < -ConvexHullRelationEpsilon)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsPointInsideConvexHull(
            List<Vector3Int> convexHull,
            Grid grid,
            Vector3Int point,
            int expectedSide
        )
        {
            if (convexHull == null || convexHull.Count == 0)
            {
                return true;
            }

            int requiredSide = expectedSide;
            Vector3 pointWorld = grid.CellToWorld(point);
            for (int i = 0; i < convexHull.Count; ++i)
            {
                Vector3Int lhs = convexHull[i];
                Vector3Int rhs = convexHull[(i + 1) % convexHull.Count];
                float relation = Geometry.IsAPointLeftOfVectorOrOnTheLine(
                    grid.CellToWorld(lhs),
                    grid.CellToWorld(rhs),
                    pointWorld
                );

                if (Mathf.Abs(relation) <= ConvexHullRelationEpsilon)
                {
                    continue;
                }

                int side = requiredSide;
                if (side == 0)
                {
                    side = 0f < relation ? 1 : -1;
                    requiredSide = side;
                }

                if (relation * side < -ConvexHullRelationEpsilon)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
