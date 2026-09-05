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
    public sealed class SpatialTree3DBoundsConsistencyTests
    {
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

        private static Bounds[] BuildBoundsSpecs(Vector3Int gridSize)
        {
            Vector3 span = new(
                Mathf.Max(gridSize.x - 1, 1),
                Mathf.Max(gridSize.y - 1, 1),
                Mathf.Max(gridSize.z - 1, 1)
            );
            Vector3 center = new(
                (gridSize.x - 1) * 0.5f,
                (gridSize.y - 1) * 0.5f,
                (gridSize.z - 1) * 0.5f
            );

            List<Bounds> specs = new()
            {
                Scale(new Vector3(1f, 1f, 1f)),
                Scale(new Vector3(0.5f, 0.5f, 0.5f)),
                Scale(new Vector3(0.25f, 0.25f, 0.25f)),
                new Bounds(center, new Vector3(1f, 1f, 1f)),
            };
            return specs.ToArray();

            Bounds Scale(Vector3 ratio)
            {
                Vector3 size = new(
                    Mathf.Max(span.x * ratio.x, 1f),
                    Mathf.Max(span.y * ratio.y, 1f),
                    Mathf.Max(span.z * ratio.z, 1f)
                );
                return new Bounds(center, size);
            }
        }

        [Test]
        public void BoundsDefinitionsOnTenAndTwentyGridsMatchAcrossTrees()
        {
            Vector3Int[] sizes = { new(10, 10, 10), new(20, 20, 20) };
            foreach (Vector3Int size in sizes)
            {
                Vector3[] points = CreateGridPoints(size);
                KdTree3D<Vector3> kd = new(points, p => p);
                OctTree3D<Vector3> oct = new(points, p => p);
                Bounds[] boundsSpecs = BuildBoundsSpecs(size);

                foreach (Bounds b in boundsSpecs)
                {
                    List<Vector3> kdResults = new();
                    kd.GetElementsInBounds(b, kdResults);

                    List<Vector3> octResults = new();
                    oct.GetElementsInBounds(b, octResults);

                    SpatialDiagnostics.AssertMatchingResults(
                        $"Bounds mismatch on grid {size}",
                        b,
                        kdResults,
                        octResults
                    );
                }
            }
        }

        [Test]
        [Timeout(15000)]
        public void FullBoundsOnHundredGridCountsMatchAcrossTrees()
        {
            Vector3Int size = new(100, 100, 100);
            Vector3[] points = CreateGridPoints(size);
            KdTree3D<Vector3> kd = new(points, p => p);
            OctTree3D<Vector3> oct = new(points, p => p);

            Bounds fullBounds = new(new Vector3(49.5f, 49.5f, 49.5f), new Vector3(99f, 99f, 99f));

            List<Vector3> kdResults = new();
            kd.GetElementsInBounds(fullBounds, kdResults);
            List<Vector3> octResults = new();
            oct.GetElementsInBounds(fullBounds, octResults);

            Assert.AreEqual(
                kdResults.Count,
                octResults.Count,
                "KDTree3D and OctTree3D returned different counts for full bounds: KD={0}, Oct={1}",
                kdResults.Count,
                octResults.Count
            );

            Assert.AreEqual(
                1_000_000,
                kdResults.Count,
                "Expected full dataset bounds to return all elements, but KD returned {0}.",
                kdResults.Count
            );

            Assert.AreEqual(
                1_000_000,
                octResults.Count,
                "Expected full dataset bounds to return all elements, but Oct returned {0}.",
                octResults.Count
            );
        }

        [Test]
        public void VariousCentersAndSizesProduceMatchingResults()
        {
            Vector3Int size = new(16, 16, 16);
            Vector3[] points = CreateGridPoints(size);
            KdTree3D<Vector3> kd = new(points, p => p);
            OctTree3D<Vector3> oct = new(points, p => p);

            List<(Vector3 Center, Vector3 Size)> cases = new()
            {
                (new Vector3(7.5f, 7.5f, 7.5f), new Vector3(1f, 1f, 1f)),
                (new Vector3(0.5f, 7.5f, 7.5f), new Vector3(1f, 1f, 1f)),
                (new Vector3(15.5f, 7.5f, 7.5f), new Vector3(1f, 1f, 1f)),
                (new Vector3(7.5f, 0.5f, 7.5f), new Vector3(1f, 1f, 1f)),
                (new Vector3(7.5f, 15.5f, 7.5f), new Vector3(1f, 1f, 1f)),
                (new Vector3(7.5f, 7.5f, 0.5f), new Vector3(1f, 1f, 1f)),
                (new Vector3(7.5f, 7.5f, 15.5f), new Vector3(1f, 1f, 1f)),
                (new Vector3(7.5f, 7.5f, 7.5f), new Vector3(3f, 1f, 2f)),
                (new Vector3(7.5f, 7.5f, 7.5f), new Vector3(5f, 2f, 1f)),
                (new Vector3(6.25f, 8.75f, 7.5f), new Vector3(2f, 2f, 2f)),
                (new Vector3(6.25f, 8.75f, 7.5f), new Vector3(1f, 3f, 5f)),
            };

            foreach ((Vector3 center, Vector3 sizeVec) in cases)
            {
                Vector3 clampedSize = new(
                    Mathf.Max(sizeVec.x, 1f),
                    Mathf.Max(sizeVec.y, 1f),
                    Mathf.Max(sizeVec.z, 1f)
                );
                Bounds b = new(center, clampedSize);

                List<Vector3> kdResults = new();
                kd.GetElementsInBounds(b, kdResults);
                List<Vector3> octResults = new();
                oct.GetElementsInBounds(b, octResults);

                SpatialDiagnostics.AssertMatchingResults(
                    "Various centers and sizes mismatch",
                    b,
                    kdResults,
                    octResults
                );
            }
        }

        [Test]
        public void SlidingWindowAlongAxesMatchesAcrossTrees()
        {
            Vector3Int grid = new(12, 12, 12);
            Vector3[] points = CreateGridPoints(grid);
            KdTree3D<Vector3> kd = new(points, p => p);
            OctTree3D<Vector3> oct = new(points, p => p);

            Vector3 baseSize = new(3f, 3f, 3f);
            for (int i = 0; i <= 9; ++i)
            {
                float c = i + 1.5f;
                Bounds bx = new(new Vector3(c, 5.5f, 5.5f), baseSize);
                Bounds by = new(new Vector3(5.5f, c, 5.5f), baseSize);
                Bounds bz = new(new Vector3(5.5f, 5.5f, c), baseSize);

                foreach (Bounds b in new[] { bx, by, bz })
                {
                    List<Vector3> kdResults = new();
                    kd.GetElementsInBounds(b, kdResults);
                    List<Vector3> octResults = new();
                    oct.GetElementsInBounds(b, octResults);

                    SpatialDiagnostics.AssertMatchingResults(
                        "Sliding window mismatch",
                        b,
                        kdResults,
                        octResults
                    );
                }
            }
        }

        [Test]
        public void UnitBoundsAtGridCenterOnTenGridConsistentWithKDTree()
        {
            Vector3Int size = new(10, 10, 10);
            Vector3[] points = CreateGridPoints(size);
            KdTree3D<Vector3> kd = new(points, p => p);
            OctTree3D<Vector3> oct = new(points, p => p);

            Bounds b = new(new Vector3(4.5f, 4.5f, 4.5f), new Vector3(1f, 1f, 1f));
            List<Vector3> kdResults = new();
            kd.GetElementsInBounds(b, kdResults);
            List<Vector3> octResults = new();
            oct.GetElementsInBounds(b, octResults);

            SpatialDiagnostics.AssertMatchingResults(
                "Unit bounds at grid center mismatch",
                b,
                kdResults,
                octResults
            );

            Assert.AreEqual(
                8,
                kdResults.Count,
                "Expected KD to return 8 points for unit bounds at grid center, got {0}.",
                kdResults.Count
            );
        }

        [Test]
        public void UnitBoundsAtGridCornerMatchesAcrossTrees()
        {
            Vector3Int size = new(10, 10, 10);
            Vector3[] points = CreateGridPoints(size);
            KdTree3D<Vector3> kd = new(points, p => p);
            OctTree3D<Vector3> oct = new(points, p => p);

            Bounds b = new(new Vector3(0.5f, 0.5f, 0.5f), Vector3.one);
            List<Vector3> kdResults = new();
            kd.GetElementsInBounds(b, kdResults);
            List<Vector3> octResults = new();
            oct.GetElementsInBounds(b, octResults);

            SpatialDiagnostics.AssertMatchingResults(
                "Unit bounds at grid corner mismatch",
                b,
                kdResults,
                octResults
            );

            Assert.AreEqual(
                8,
                kdResults.Count,
                "Expected KD to return 8 points for unit bounds at grid corner, got {0}.",
                kdResults.Count
            );
        }

        [Test]
        public void UnitBoundsCenteredOnEdgeMidpointMatchesAcrossTrees()
        {
            Vector3Int size = new(10, 10, 10);
            Vector3[] points = CreateGridPoints(size);
            KdTree3D<Vector3> kd = new(points, p => p);
            OctTree3D<Vector3> oct = new(points, p => p);

            Bounds b = new(new Vector3(5f, 4.5f, 4.5f), Vector3.one);
            List<Vector3> kdResults = new();
            kd.GetElementsInBounds(b, kdResults);
            List<Vector3> octResults = new();
            oct.GetElementsInBounds(b, octResults);

            SpatialDiagnostics.AssertMatchingResults(
                "Unit bounds at edge midpoint mismatch",
                b,
                kdResults,
                octResults
            );

            Assert.AreEqual(
                4,
                kdResults.Count,
                "Expected KD to return 4 points for unit bounds centered on an edge midpoint, got {0}.",
                kdResults.Count
            );
        }

        [Test]
        public void EdgeTouchingBoundsConsistentAcrossTrees()
        {
            Vector3Int size = new(10, 10, 10);
            Vector3[] points = CreateGridPoints(size);
            KdTree3D<Vector3> kd = new(points, p => p);
            OctTree3D<Vector3> oct = new(points, p => p);

            Bounds[] cases =
            {
                new(new Vector3(0.5f, 4.5f, 4.5f), new Vector3(1f, 1f, 1f)),
                new(new Vector3(9.5f, 4.5f, 4.5f), new Vector3(1f, 1f, 1f)),
                new(new Vector3(4.5f, 0.5f, 4.5f), new Vector3(1f, 1f, 1f)),
                new(new Vector3(4.5f, 9.5f, 4.5f), new Vector3(1f, 1f, 1f)),
                new(new Vector3(4.5f, 4.5f, 0.5f), new Vector3(1f, 1f, 1f)),
                new(new Vector3(4.5f, 4.5f, 9.5f), new Vector3(1f, 1f, 1f)),
            };

            foreach (Bounds b in cases)
            {
                List<Vector3> kdResults = new();
                kd.GetElementsInBounds(b, kdResults);
                List<Vector3> octResults = new();
                oct.GetElementsInBounds(b, octResults);

                SpatialDiagnostics.AssertMatchingResults(
                    "Edge touching bounds mismatch",
                    b,
                    kdResults,
                    octResults
                );
            }
        }

        [Test]
        public void RotatedBoundsProjectionMatchesAcrossTrees()
        {
            Vector3Int size = new(18, 18, 18);
            Vector3[] points = CreateGridPoints(size);
            KdTree3D<Vector3> kd = new(points, p => p);
            OctTree3D<Vector3> oct = new(points, p => p);

            Bounds baseBounds = new(new Vector3(8.5f, 8.5f, 8.5f), new Vector3(6f, 4f, 5f));
            Quaternion[] rotations =
            {
                Quaternion.identity,
                Quaternion.Euler(0f, 15f, 0f),
                Quaternion.Euler(10f, 25f, 5f),
                Quaternion.Euler(30f, 12f, 22f),
            };

            List<Vector3> kdResults = new();
            List<Vector3> octResults = new();
            for (int i = 0; i < rotations.Length; ++i)
            {
                Bounds projected = BuildAxisAlignedBounds(baseBounds, rotations[i]);
                kd.GetElementsInBounds(projected, kdResults);
                oct.GetElementsInBounds(projected, octResults);

                SpatialDiagnostics.AssertMatchingResults(
                    $"Rotated bounds projection mismatch #{i}",
                    projected,
                    kdResults,
                    octResults
                );
            }
        }

        [Test]
        [Timeout(20000)]
        public void LargeGridRandomBoundsFuzzMatchesAcrossTrees()
        {
            Vector3Int size = new(32, 32, 32);
            Vector3[] points = CreateGridPoints(size);
            KdTree3D<Vector3> kd = new(points, p => p);
            OctTree3D<Vector3> oct = new(points, p => p);

            IRandom random = new PcgRandom(0xBADC0DE);
            List<Vector3> kdResults = new();
            List<Vector3> octResults = new();

            for (int i = 0; i < 128; ++i)
            {
                Bounds query = CreateRandomBounds(random, size);
                kd.GetElementsInBounds(query, kdResults);
                oct.GetElementsInBounds(query, octResults);

                SpatialDiagnostics.AssertMatchingResults(
                    $"Random bounds fuzz mismatch #{i}",
                    query,
                    kdResults,
                    octResults,
                    maxItems: 128
                );
            }
        }

        private static Bounds BuildAxisAlignedBounds(Bounds source, Quaternion rotation)
        {
            Vector3 extents = source.extents;
            Vector3 min = new(
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity
            );
            Vector3 max = new(
                float.NegativeInfinity,
                float.NegativeInfinity,
                float.NegativeInfinity
            );

            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        Vector3 offset = new(extents.x * sx, extents.y * sy, extents.z * sz);
                        Vector3 rotated = rotation * offset;
                        Vector3 point = source.center + rotated;
                        if (point.x < min.x)
                        {
                            min.x = point.x;
                        }
                        if (point.y < min.y)
                        {
                            min.y = point.y;
                        }
                        if (point.z < min.z)
                        {
                            min.z = point.z;
                        }
                        if (max.x < point.x)
                        {
                            max.x = point.x;
                        }
                        if (max.y < point.y)
                        {
                            max.y = point.y;
                        }
                        if (max.z < point.z)
                        {
                            max.z = point.z;
                        }
                    }
                }
            }

            Vector3 size = max - min;
            Vector3 center = (min + max) * 0.5f;
            return new Bounds(center, size);
        }

        private static Bounds CreateRandomBounds(IRandom random, Vector3Int gridSize)
        {
            Vector3 center = new(
                RandomRange(random, -2f, gridSize.x + 2f),
                RandomRange(random, -2f, gridSize.y + 2f),
                RandomRange(random, -2f, gridSize.z + 2f)
            );
            Vector3 size = new(
                Mathf.Max(1f, RandomRange(random, 1f, gridSize.x)),
                Mathf.Max(1f, RandomRange(random, 1f, gridSize.y)),
                Mathf.Max(1f, RandomRange(random, 1f, gridSize.z))
            );
            return new Bounds(center, size);
        }

        private static float RandomRange(IRandom random, float min, float max)
        {
            return (float)(min + random.NextDouble() * (max - min));
        }
    }
}
