// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;
    using Vector2 = UnityEngine.Vector2;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SpatialTree2DBoundsFuzzTests
    {
        /*
            A fixed seed, not PRNG.Instance: that hands out an instance seeded from Guid.NewGuid(),
            so a red fuzz iteration in CI cannot be reproduced. The seed rides in every assertion
            message, which is what makes the failing run replayable from the log alone.
        */
        private const uint RandomSeed = 0x5EED2D01;

        private static Vector2[] CreateGridPoints(Vector2Int gridSize)
        {
            int total = gridSize.x * gridSize.y;
            Vector2[] points = new Vector2[total];
            int width = gridSize.x;
            int height = gridSize.y;
            int index = 0;
            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    points[index++] = new Vector2(x, y);
                }
            }
            return points;
        }

        [Test]
        public void RandomBoundsOnSmallGridMatchAcrossTrees()
        {
            Vector2Int grid = new(12, 12);
            Vector2[] points = CreateGridPoints(grid);
            KdTree2D<Vector2> balancedKd = new(points, p => p, balanced: true);
            KdTree2D<Vector2> unbalancedKd = new(points, p => p, balanced: false);
            QuadTree2D<Vector2> quad = new(points, p => p);

            IRandom prng = new PcgRandom(RandomSeed);
            for (int i = 0; i < 250; ++i)
            {
                Vector2 center = new(
                    prng.NextFloat(0f, grid.x - 1),
                    prng.NextFloat(0f, grid.y - 1)
                );

                Vector2 size = new(
                    Mathf.Max(1f, prng.NextFloat(0.1f, grid.x - 1)),
                    Mathf.Max(1f, prng.NextFloat(0.1f, grid.y - 1))
                );

                Bounds b = new(center, new Vector3(size.x, size.y, 1f));
                List<Vector2> balancedKdResults = new();
                balancedKd.GetElementsInBounds(b, balancedKdResults);
                List<Vector2> unbalancedKdResults = new();
                unbalancedKd.GetElementsInBounds(b, unbalancedKdResults);
                List<Vector2> quadResults = new();
                quad.GetElementsInBounds(b, quadResults);

                List<Vector3> balancedKd3D = ConvertToVector3(balancedKdResults);
                List<Vector3> unbalancedKd3D = ConvertToVector3(unbalancedKdResults);
                List<Vector3> quad3D = ConvertToVector3(quadResults);

                SpatialDiagnostics.AssertMatchingResults(
                    $"Random bounds mismatch at iteration {i} of seed 0x{RandomSeed:X8} (BalancedKD vs UnbalancedKD)",
                    b,
                    balancedKd3D,
                    unbalancedKd3D
                );

                SpatialDiagnostics.AssertMatchingResults(
                    $"Random bounds mismatch at iteration {i} of seed 0x{RandomSeed:X8} (BalancedKD vs QuadTree)",
                    b,
                    balancedKd3D,
                    quad3D
                );
            }
        }

        [Test]
        public void MicroShiftedBoundsAroundGridCentersMatchAcrossTrees()
        {
            Vector2Int grid = new(10, 10);
            Vector2[] points = CreateGridPoints(grid);
            KdTree2D<Vector2> balancedKd = new(points, p => p, balanced: true);
            KdTree2D<Vector2> unbalancedKd = new(points, p => p, balanced: false);
            QuadTree2D<Vector2> quad = new(points, p => p);

            Vector2[] deltas = { new(-0.01f, 0f), new(0.01f, 0f), new(0f, -0.01f), new(0f, 0.01f) };

            Vector2 baseCenter = new(4.5f, 4.5f);
            Vector2 baseSize = new(1f, 1f);

            foreach (Vector2 delta in deltas)
            {
                Bounds b = new(baseCenter + delta, new Vector3(baseSize.x, baseSize.y, 1f));
                List<Vector2> balancedKdResults = new();
                balancedKd.GetElementsInBounds(b, balancedKdResults);
                List<Vector2> unbalancedKdResults = new();
                unbalancedKd.GetElementsInBounds(b, unbalancedKdResults);
                List<Vector2> quadResults = new();
                quad.GetElementsInBounds(b, quadResults);

                List<Vector3> balancedKd3D = ConvertToVector3(balancedKdResults);
                List<Vector3> unbalancedKd3D = ConvertToVector3(unbalancedKdResults);
                List<Vector3> quad3D = ConvertToVector3(quadResults);

                SpatialDiagnostics.AssertMatchingResults(
                    $"Micro-shift mismatch at delta={delta} (BalancedKD vs UnbalancedKD)",
                    b,
                    balancedKd3D,
                    unbalancedKd3D
                );

                SpatialDiagnostics.AssertMatchingResults(
                    $"Micro-shift mismatch at delta={delta} (BalancedKD vs QuadTree)",
                    b,
                    balancedKd3D,
                    quad3D
                );
            }
        }

        [Test]
        public void RandomUnitBoundsOnGridCentersMatchAcrossTrees()
        {
            Vector2Int grid = new(10, 10);
            Vector2[] points = CreateGridPoints(grid);
            KdTree2D<Vector2> balancedKd = new(points, p => p, balanced: true);
            KdTree2D<Vector2> unbalancedKd = new(points, p => p, balanced: false);
            QuadTree2D<Vector2> quad = new(points, p => p);

            IRandom prng = new PcgRandom(RandomSeed);
            for (int i = 0; i < 64; ++i)
            {
                int cx = prng.Next(0, grid.x - 1);
                int cy = prng.Next(0, grid.y - 1);
                Bounds b = new(new Vector3(cx + 0.5f, cy + 0.5f, 0f), new Vector3(1f, 1f, 1f));

                List<Vector2> balancedKdResults = new();
                balancedKd.GetElementsInBounds(b, balancedKdResults);
                List<Vector2> unbalancedKdResults = new();
                unbalancedKd.GetElementsInBounds(b, unbalancedKdResults);
                List<Vector2> quadResults = new();
                quad.GetElementsInBounds(b, quadResults);

                List<Vector3> balancedKd3D = ConvertToVector3(balancedKdResults);
                List<Vector3> unbalancedKd3D = ConvertToVector3(unbalancedKdResults);
                List<Vector3> quad3D = ConvertToVector3(quadResults);

                SpatialDiagnostics.AssertMatchingResults(
                    $"Random unit center mismatch at iteration {i} of seed 0x{RandomSeed:X8} (BalancedKD vs UnbalancedKD)",
                    b,
                    balancedKd3D,
                    unbalancedKd3D
                );

                SpatialDiagnostics.AssertMatchingResults(
                    $"Random unit center mismatch at iteration {i} of seed 0x{RandomSeed:X8} (BalancedKD vs QuadTree)",
                    b,
                    balancedKd3D,
                    quad3D
                );
            }
        }

        [Test]
        public void EpsilonAdjustedBoundsOnEdgesMatchAcrossTrees()
        {
            Vector2Int grid = new(10, 10);
            Vector2[] points = CreateGridPoints(grid);
            KdTree2D<Vector2> balancedKd = new(points, p => p, balanced: true);
            KdTree2D<Vector2> unbalancedKd = new(points, p => p, balanced: false);
            QuadTree2D<Vector2> quad = new(points, p => p);

            float[] eps = { -0.01f, -0.001f, 0f, 0.001f, 0.01f };
            foreach (float e in eps)
            {
                Bounds b = new(new Vector3(4.5f, 4.5f + e, 0f), new Vector3(1f, 1f, 1f));

                List<Vector2> balancedKdResults = new();
                balancedKd.GetElementsInBounds(b, balancedKdResults);
                List<Vector2> unbalancedKdResults = new();
                unbalancedKd.GetElementsInBounds(b, unbalancedKdResults);
                List<Vector2> quadResults = new();
                quad.GetElementsInBounds(b, quadResults);

                List<Vector3> balancedKd3D = ConvertToVector3(balancedKdResults);
                List<Vector3> unbalancedKd3D = ConvertToVector3(unbalancedKdResults);
                List<Vector3> quad3D = ConvertToVector3(quadResults);

                SpatialDiagnostics.AssertMatchingResults(
                    "Epsilon adjusted mismatch (BalancedKD vs UnbalancedKD)",
                    b,
                    balancedKd3D,
                    unbalancedKd3D
                );

                SpatialDiagnostics.AssertMatchingResults(
                    "Epsilon adjusted mismatch (BalancedKD vs QuadTree)",
                    b,
                    balancedKd3D,
                    quad3D
                );
            }
        }

        private static List<Vector3> ConvertToVector3(List<Vector2> points)
        {
            List<Vector3> result = new(points.Count);
            foreach (Vector2 p in points)
            {
                result.Add(new Vector3(p.x, p.y, 0f));
            }
            return result;
        }
    }
}
