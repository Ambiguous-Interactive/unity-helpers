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
    using Vector3 = UnityEngine.Vector3;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SpatialTree3DBoundsFuzzTests
    {
        /*
            A fixed seed, not PRNG.Instance: that hands out an instance seeded from Guid.NewGuid(),
            so a red fuzz iteration in CI cannot be reproduced. The seed rides in every assertion
            message, which is what makes the failing run replayable from the log alone.
        */
        private const uint RandomSeed = 0x5EED3D01;

        private static Vector3[] CreateGridPoints(Vector3Int gridSize)
        {
            int total = gridSize.x * gridSize.y * gridSize.z;
            Vector3[] points = new Vector3[total];
            int width = gridSize.x;
            int height = gridSize.y;
            int depth = gridSize.z;
            int index = 0;
            for (int z = 0; z < depth; ++z)
            {
                for (int y = 0; y < height; ++y)
                {
                    for (int x = 0; x < width; ++x)
                    {
                        points[index++] = new Vector3(x, y, z);
                    }
                }
            }
            return points;
        }

        [Test]
        public void RandomBoundsOnSmallGridMatchAcrossTrees()
        {
            Vector3Int grid = new(12, 12, 12);
            Vector3[] points = CreateGridPoints(grid);
            KdTree3D<Vector3> kd = new(points, p => p);
            OctTree3D<Vector3> oct = new(points, p => p);

            IRandom prng = new PcgRandom(RandomSeed);
            for (int i = 0; i < 250; ++i)
            {
                Vector3 center = new(
                    prng.NextFloat(0f, grid.x - 1),
                    prng.NextFloat(0f, grid.y - 1),
                    prng.NextFloat(0f, grid.z - 1)
                );

                Vector3 size = new(
                    Mathf.Max(1f, prng.NextFloat(0.1f, grid.x - 1)),
                    Mathf.Max(1f, prng.NextFloat(0.1f, grid.y - 1)),
                    Mathf.Max(1f, prng.NextFloat(0.1f, grid.z - 1))
                );

                Bounds b = new(center, size);
                List<Vector3> kdResults = new();
                kd.GetElementsInBounds(b, kdResults);
                List<Vector3> octResults = new();
                oct.GetElementsInBounds(b, octResults);

                SpatialDiagnostics.AssertMatchingResults(
                    $"Random bounds mismatch at iteration {i} of seed 0x{RandomSeed:X8}",
                    b,
                    kdResults,
                    octResults
                );
            }
        }

        [Test]
        public void MicroShiftedBoundsAroundGridCentersMatchAcrossTrees()
        {
            Vector3Int grid = new(10, 10, 10);
            Vector3[] points = CreateGridPoints(grid);
            KdTree3D<Vector3> kd = new(points, p => p);
            OctTree3D<Vector3> oct = new(points, p => p);

            Vector3[] deltas =
            {
                new(-0.01f, 0f, 0f),
                new(0.01f, 0f, 0f),
                new(0f, -0.01f, 0f),
                new(0f, 0.01f, 0f),
                new(0f, 0f, -0.01f),
                new(0f, 0f, 0.01f),
            };

            Vector3 baseCenter = new(4.5f, 4.5f, 4.5f);
            Vector3 baseSize = new(1f, 1f, 1f);

            foreach (Vector3 delta in deltas)
            {
                Bounds b = new(baseCenter + delta, baseSize);
                List<Vector3> kdResults = new();
                kd.GetElementsInBounds(b, kdResults);
                List<Vector3> octResults = new();
                oct.GetElementsInBounds(b, octResults);

                SpatialDiagnostics.AssertMatchingResults(
                    $"Micro-shift mismatch at delta={delta}",
                    b,
                    kdResults,
                    octResults
                );
            }
        }

        [Test]
        public void RandomUnitBoundsOnGridCentersMatchAcrossTrees()
        {
            Vector3Int grid = new(10, 10, 10);
            Vector3[] points = CreateGridPoints(grid);
            KdTree3D<Vector3> kd = new(points, p => p);
            OctTree3D<Vector3> oct = new(points, p => p);

            IRandom prng = new PcgRandom(RandomSeed);
            for (int i = 0; i < 64; ++i)
            {
                int cx = prng.Next(0, grid.x - 1);
                int cy = prng.Next(0, grid.y - 1);
                int cz = prng.Next(0, grid.z - 1);
                Bounds b = new(
                    new Vector3(cx + 0.5f, cy + 0.5f, cz + 0.5f),
                    new Vector3(1f, 1f, 1f)
                );

                List<Vector3> kdResults = new();
                kd.GetElementsInBounds(b, kdResults);
                List<Vector3> octResults = new();
                oct.GetElementsInBounds(b, octResults);

                SpatialDiagnostics.AssertMatchingResults(
                    $"Random unit center mismatch at iteration {i} of seed 0x{RandomSeed:X8}",
                    b,
                    kdResults,
                    octResults
                );
            }
        }

        [Test]
        public void EpsilonAdjustedBoundsOnEdgesMatchAcrossTrees()
        {
            Vector3Int grid = new(10, 10, 10);
            Vector3[] points = CreateGridPoints(grid);
            KdTree3D<Vector3> kd = new(points, p => p);
            OctTree3D<Vector3> oct = new(points, p => p);

            float[] eps = { -0.01f, -0.001f, 0f, 0.001f, 0.01f };
            foreach (float e in eps)
            {
                Bounds b = new(new Vector3(4.5f, 4.5f + e, 4.5f), new Vector3(1f, 1f, 1f));

                List<Vector3> kdResults = new();
                kd.GetElementsInBounds(b, kdResults);
                List<Vector3> octResults = new();
                oct.GetElementsInBounds(b, octResults);

                SpatialDiagnostics.AssertMatchingResults(
                    "Epsilon adjusted mismatch",
                    b,
                    kdResults,
                    octResults
                );
            }
        }
    }
}
