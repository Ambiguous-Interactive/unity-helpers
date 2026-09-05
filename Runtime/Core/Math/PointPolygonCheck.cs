// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Math
{
    using System;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Provides methods for determining if a point is inside a polygon.
    /// </summary>
    public static class PointPolygonCheck
    {
        /// <summary>
        /// Determines if a 2D point is inside a polygon using the ray-casting algorithm.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <param name="polygon">The vertices of the polygon in order (clockwise or counter-clockwise).</param>
        /// <returns>
        /// <c>true</c> if the point is inside the polygon; otherwise, <c>false</c>.
        /// Returns <c>false</c> for null or degenerate polygons (fewer than 3 vertices).
        /// </returns>
        /// <remarks>
        /// Points exactly on polygon edges or vertices may return inconsistent results due to floating-point precision.
        /// The polygon is assumed to be simple (non-self-intersecting) for predictable results.
        /// </remarks>
        public static bool IsPointInsidePolygon(Vector2 point, Vector2[] polygon)
        {
            if (polygon == null || polygon.Length < 3)
            {
                return false;
            }

            return IsPointInsidePolygon(point, new ReadOnlySpan<Vector2>(polygon));
        }

        /// <summary>
        /// Determines if a 2D point is inside a polygon using the ray-casting algorithm.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <param name="polygon">The vertices of the polygon in order (clockwise or counter-clockwise).</param>
        /// <returns>
        /// <c>true</c> if the point is inside the polygon; otherwise, <c>false</c>.
        /// Returns <c>false</c> for degenerate polygons (fewer than 3 vertices).
        /// </returns>
        /// <remarks>
        /// Points exactly on polygon edges or vertices may return inconsistent results due to floating-point precision.
        /// The polygon is assumed to be simple (non-self-intersecting) for predictable results.
        /// This overload is more performant as it avoids array allocations.
        /// </remarks>
        public static bool IsPointInsidePolygon(Vector2 point, ReadOnlySpan<Vector2> polygon)
        {
            if (polygon.Length < 3)
            {
                return false;
            }

            bool inside = false;
            int j = polygon.Length - 1;

            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 vi = polygon[i];
                Vector2 vj = polygon[j];

                if ((vi.y < point.y && point.y <= vj.y) || (vj.y < point.y && point.y <= vi.y))
                {
                    float intersectX = vi.x + (point.y - vi.y) / (vj.y - vi.y) * (vj.x - vi.x);

                    if (intersectX < point.x)
                    {
                        inside = !inside;
                    }
                }

                j = i;
            }

            return inside;
        }

        /// <summary>
        /// Determines if a 3D point is inside a 3D polygon by projecting onto a plane.
        /// </summary>
        /// <param name="point">The 3D point to test.</param>
        /// <param name="polygon">The vertices of the 3D polygon in order (must be coplanar).</param>
        /// <param name="planeNormal">The normal vector of the plane containing the polygon. Must be normalized.</param>
        /// <returns>
        /// <c>true</c> if the point (projected onto the polygon's plane) is inside the polygon; otherwise, <c>false</c>.
        /// Returns <c>false</c> for null or degenerate polygons (fewer than 3 vertices).
        /// </returns>
        /// <remarks>
        /// The point is first projected onto the plane defined by the polygon and plane normal.
        /// Then a 2D point-in-polygon test is performed using a coordinate system aligned with the plane.
        /// The polygon vertices must be coplanar for accurate results.
        /// </remarks>
        public static bool IsPointInsidePolygon(
            Vector3 point,
            Vector3[] polygon,
            Vector3 planeNormal
        )
        {
            if (polygon == null || polygon.Length < 3)
            {
                return false;
            }

            return IsPointInsidePolygon(point, new ReadOnlySpan<Vector3>(polygon), planeNormal);
        }

        /// <summary>
        /// Determines if a 3D point is inside a 3D polygon by projecting onto a plane.
        /// </summary>
        /// <param name="point">The 3D point to test.</param>
        /// <param name="polygon">The vertices of the 3D polygon in order (must be coplanar).</param>
        /// <param name="planeNormal">The normal vector of the plane containing the polygon. Must be normalized.</param>
        /// <returns>
        /// <c>true</c> if the point (projected onto the polygon's plane) is inside the polygon; otherwise, <c>false</c>.
        /// Returns <c>false</c> for degenerate polygons (fewer than 3 vertices).
        /// </returns>
        /// <remarks>
        /// The point is first projected onto the plane defined by the polygon and plane normal.
        /// Then a 2D point-in-polygon test is performed using a coordinate system aligned with the plane.
        /// The polygon vertices must be coplanar for accurate results.
        /// This overload is more performant as it avoids array allocations.
        /// </remarks>
        public static bool IsPointInsidePolygon(
            Vector3 point,
            ReadOnlySpan<Vector3> polygon,
            Vector3 planeNormal
        )
        {
            if (polygon.Length < 3)
            {
                return false;
            }

            Vector3 tangent;
            if (0.9f < Mathf.Abs(planeNormal.x))
            {
                tangent = Vector3.Cross(planeNormal, Vector3.up).normalized;
            }
            else
            {
                tangent = Vector3.Cross(planeNormal, Vector3.right).normalized;
            }
            Vector3 bitangent = Vector3.Cross(planeNormal, tangent);

            Vector3 origin = polygon[0];
            Vector3 relativePoint = point - origin;

            Vector2 point2D = new(
                Vector3.Dot(relativePoint, tangent),
                Vector3.Dot(relativePoint, bitangent)
            );

            int vertexCount = polygon.Length;
            Span<Vector2> polygon2D =
                vertexCount <= MaxStackVertexCount ? stackalloc Vector2[vertexCount] : default;
            if (!polygon2D.IsEmpty)
            {
                ProjectOntoPlane(polygon, origin, tangent, bitangent, polygon2D);
                return IsPointInsidePolygon(point2D, polygon2D);
            }

            using PooledArray<Vector2> pooled = SystemArrayPool<Vector2>.Get(
                vertexCount,
                out Vector2[] rented
            );
            Span<Vector2> projected = rented.AsSpan(0, vertexCount);
            ProjectOntoPlane(polygon, origin, tangent, bitangent, projected);
            return IsPointInsidePolygon(point2D, projected);
        }

        private static void ProjectOntoPlane(
            ReadOnlySpan<Vector3> polygon,
            Vector3 origin,
            Vector3 tangent,
            Vector3 bitangent,
            Span<Vector2> destination
        )
        {
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector3 relativeVertex = polygon[i] - origin;
                destination[i] = new Vector2(
                    Vector3.Dot(relativeVertex, tangent),
                    Vector3.Dot(relativeVertex, bitangent)
                );
            }
        }

        private const int MaxStackVertexCount = StackAllocation.MaxByteBudget / VertexByteCount;

        private const int VertexByteCount = 8;
    }
}
