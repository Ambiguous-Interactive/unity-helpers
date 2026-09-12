// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace Samples.UnityHelpers.SpatialStructures
{
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Extension;

    /// <summary>
    /// Demonstrates when to pick gridless (Vector2) versus grid-aware (Grid + FastVector3Int) hull helpers.
    /// Attach this component to any GameObject, assign a Grid reference for the tile-based example, and press Play.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HullUsageDemo : MonoBehaviour
    {
        private const float HullLineDuration = 8f;

        [Header("Gridless (Vector2)")]
        [SerializeField]
#pragma warning disable WPROTO028 // Inspector-only sample input; this component is not a proto contract.
        private Vector2 gridlessBounds = new Vector2(10f, 5f);
#pragma warning restore WPROTO028

        [SerializeField]
        [Range(4, 32)]
        private int gridlessEdgeSamplesPerSide = 8;

        [Header("Grid-aware (Grid + FastVector3Int)")]
        [SerializeField]
        private Grid grid;

        [SerializeField]
#pragma warning disable WPROTO028 // Inspector-only sample input; this component is not a proto contract.
        private Vector2Int gridFootprint = new Vector2Int(6, 4);
#pragma warning restore WPROTO028

        [SerializeField]
        [Range(3, 12)]
        private int gridHullNeighbors = 5;

        private void Start()
        {
            List<Vector2> gridlessHull = RunGridlessExample();
            DrawGridlessHull(gridlessHull);

            List<FastVector3Int> gridHull = RunGridAwareExample();
            DrawGridAwareHull(gridHull);
        }

        private List<Vector2> RunGridlessExample()
        {
            List<Vector2> pointCloud = CreateGridlessPointCloud();
            UnityExtensions.ConcaveHullOptions options = UnityExtensions
                .ConcaveHullOptions.Default.WithStrategy(
                    UnityExtensions.ConcaveHullStrategy.EdgeSplit
                )
                .WithBucketSize(Mathf.Max(16, pointCloud.Count / 2))
                .WithAngleThreshold(65f);
            List<Vector2> hull = pointCloud.BuildConcaveHull(options);
            Debug.Log(
                $"[HullUsageDemo] Gridless hull: {hull.Count} vertices from {pointCloud.Count} samples using {options.Strategy}."
            );
            return hull;
        }

        private List<Vector2> CreateGridlessPointCloud()
        {
            List<Vector2> points = new List<Vector2>();
            float halfWidth = gridlessBounds.x * 0.5f;
            float halfHeight = gridlessBounds.y * 0.5f;
            int horizontalSamples = Mathf.Max(2, gridlessEdgeSamplesPerSide);

            for (int i = 0; i < horizontalSamples; i++)
            {
                float t = i / (float)(horizontalSamples - 1);
                float x = Mathf.Lerp(-halfWidth, halfWidth, t);
                points.Add(new Vector2(x, -halfHeight));
                points.Add(new Vector2(x * 0.7f, halfHeight));
            }

            for (int i = 1; i < horizontalSamples - 1; i++)
            {
                float t = i / (float)(horizontalSamples - 1);
                float y = Mathf.Lerp(-halfHeight * 0.75f, halfHeight * 0.75f, t);
                points.Add(new Vector2(-halfWidth, y));
                points.Add(new Vector2(halfWidth, y * 0.4f));
            }

            points.Add(new Vector2(0f, halfHeight + 1.25f));
            points.Add(new Vector2(-halfWidth * 0.2f, halfHeight + 0.75f));
            points.Add(new Vector2(halfWidth * 0.2f, halfHeight + 0.75f));
            return points;
        }

        private void DrawGridlessHull(IReadOnlyList<Vector2> hull)
        {
            if (hull == null || hull.Count < 2)
            {
                return;
            }

#pragma warning disable WPROTO028 // Transient debug geometry is never serialized by WallstopProto.
            List<Vector3> loop = new List<Vector3>(hull.Count);
            for (int i = 0; i < hull.Count; i++)
            {
                Vector2 point = hull[i];
                loop.Add(new Vector3(point.x, point.y, 0f));
            }

            DrawLoop(loop, Color.cyan);
#pragma warning restore WPROTO028
        }

        private List<FastVector3Int> RunGridAwareExample()
        {
            if (grid == null)
            {
                Debug.LogWarning(
                    "[HullUsageDemo] Assign a Grid reference to show the grid-aware hull example."
                );
                return new List<FastVector3Int>();
            }

            List<FastVector3Int> tileSamples = CreateGridPointCloud();
            UnityExtensions.ConcaveHullOptions options = UnityExtensions
                .ConcaveHullOptions.Default.WithStrategy(UnityExtensions.ConcaveHullStrategy.Knn)
                .WithNearestNeighbors(Mathf.Max(3, gridHullNeighbors));
            List<FastVector3Int> hull = tileSamples.BuildConcaveHull(grid, options);
            Debug.Log(
                $"[HullUsageDemo] Grid-aware hull: {hull.Count} tiles from {tileSamples.Count} samples using Grid \"{grid.name}\"."
            );
            return hull;
        }

        private List<FastVector3Int> CreateGridPointCloud()
        {
            int width = Mathf.Max(3, gridFootprint.x);
            int height = Mathf.Max(3, gridFootprint.y);
            List<FastVector3Int> tiles = new List<FastVector3Int>(width * height);

            for (int x = 0; x < width; x++)
            {
                tiles.Add(new FastVector3Int(x, 0, 0));
                tiles.Add(new FastVector3Int(x, height - 1, 0));
            }

            for (int y = 1; y < height - 1; y++)
            {
                tiles.Add(new FastVector3Int(0, y, 0));
                tiles.Add(new FastVector3Int(width - 1, y, 0));
            }

            tiles.Add(new FastVector3Int(width / 2, height / 2, 0));
            tiles.Add(new FastVector3Int(width / 2 - 1, height / 2, 0));
            tiles.Add(new FastVector3Int(width / 2, height / 2 - 1, 0));
            return tiles;
        }

        private void DrawGridAwareHull(IReadOnlyList<FastVector3Int> hull)
        {
            if (grid == null || hull == null || hull.Count < 2)
            {
                return;
            }

#pragma warning disable WPROTO028 // Transient debug geometry is never serialized by WallstopProto.
            List<Vector3> loop = new List<Vector3>(hull.Count);
            for (int i = 0; i < hull.Count; i++)
            {
                Vector3 worldPoint = grid.CellToWorld((Vector3Int)hull[i]);
                loop.Add(worldPoint);
            }

            DrawLoop(loop, Color.yellow);
#pragma warning restore WPROTO028
        }

        private static void DrawLoop(IReadOnlyList<Vector3> points, Color color)
        {
            if (points == null || points.Count < 2)
            {
                return;
            }

            for (int i = 0; i < points.Count; i++)
            {
                Vector3 start = points[i];
                Vector3 end = points[(i + 1) % points.Count];
                Debug.DrawLine(start, end, color, HullLineDuration);
            }
        }
    }
}
