// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace Samples.UnityHelpers.SpatialStructures
{
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Random;

    /// <summary>
    /// Demonstrates QuadTree2D, KdTree2D, and SpatialHash2D with random points.
    /// </summary>
    public sealed class SpatialStructuresDemo : MonoBehaviour
    {
        [SerializeField]
        private int pointCount = 100;

        [SerializeField]
        private Vector2 areaSize = new Vector2(50f, 50f);

        [SerializeField]
        private float queryRadius = 5f;

        private void Start()
        {
            IRandom rng = PRNG.Instance;
            List<Vector2> points = new List<Vector2>(pointCount);
            for (int i = 0; i < pointCount; i++)
            {
                float x = rng.NextFloat(-areaSize.x * 0.5f, areaSize.x * 0.5f);
                float y = rng.NextFloat(-areaSize.y * 0.5f, areaSize.y * 0.5f);
                points.Add(new Vector2(x, y));
            }

#pragma warning disable WPROTO028 // Transient query bounds are never serialized by WallstopProto.
            Bounds bounds = new Bounds(Vector3.zero, new Vector3(areaSize.x, areaSize.y, 0.1f));
#pragma warning restore WPROTO028

            // QuadTree: radius query
            QuadTree2D<Vector2> quad = new QuadTree2D<Vector2>(points, p => p, bounds);
            List<Vector2> results = new List<Vector2>();
            Vector2 origin = Vector2.zero;
            quad.GetElementsInRange(origin, queryRadius, results);
            Debug.Log($"QuadTree2D found {results.Count} points within {queryRadius} of {origin}");

            // KdTree: approximate nearest neighbors
            KdTree2D<Vector2> kd = new KdTree2D<Vector2>(points, p => p);
            results.Clear();
            kd.GetApproximateNearestNeighbors(origin, 8, results);
            Debug.Log($"KdTree2D nearest neighbors (approx): {results.Count}");

            // SpatialHash: insert and query
            using (ISpatialHash2D<Vector2> grid = new SpatialHash2D<Vector2>(cellSize: queryRadius))
            {
                foreach (Vector2 p in points)
                {
                    grid.Insert(p, p);
                }

                results.Clear();
                grid.Query(origin, queryRadius, results);
                Debug.Log($"SpatialHash2D found {results.Count} points within {queryRadius}");
            }
        }
    }
}
