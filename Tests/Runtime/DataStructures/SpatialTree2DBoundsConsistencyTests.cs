// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;
    using Vector2 = UnityEngine.Vector2;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SpatialTree2DBoundsConsistencyTests
    {
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

        private static Bounds[] BuildBoundsSpecs(Vector2Int gridSize)
        {
            Vector2 span = new(Mathf.Max(gridSize.x - 1, 1), Mathf.Max(gridSize.y - 1, 1));
            Vector2 center = new((gridSize.x - 1) * 0.5f, (gridSize.y - 1) * 0.5f);

            List<Bounds> specs = new()
            {
                Scale(new Vector2(1f, 1f)),
                Scale(new Vector2(0.5f, 0.5f)),
                Scale(new Vector2(0.25f, 0.25f)),
                new Bounds(center, new Vector3(1f, 1f, 1f)),
            };
            return specs.ToArray();

            Bounds Scale(Vector2 ratio)
            {
                Vector2 size = new(
                    Mathf.Max(span.x * ratio.x, 1f),
                    Mathf.Max(span.y * ratio.y, 1f)
                );
                return new Bounds(center, new Vector3(size.x, size.y, 1f));
            }
        }

        [Test]
        public void BoundsDefinitionsOnTenAndTwentyGridsMatchAcrossTrees()
        {
            Vector2Int[] sizes = { new(10, 10), new(20, 20) };
            foreach (Vector2Int size in sizes)
            {
                Vector2[] points = CreateGridPoints(size);
                KdTree2D<Vector2> balancedKd = new(points, p => p, balanced: true);
                KdTree2D<Vector2> unbalancedKd = new(points, p => p, balanced: false);
                QuadTree2D<Vector2> quad = new(points, p => p);
                Bounds[] boundsSpecs = BuildBoundsSpecs(size);

                foreach (Bounds b in boundsSpecs)
                {
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
                        $"BalancedKD vs UnbalancedKD mismatch on grid {size}",
                        b,
                        balancedKd3D,
                        unbalancedKd3D
                    );

                    SpatialDiagnostics.AssertMatchingResults(
                        $"BalancedKD vs QuadTree mismatch on grid {size}",
                        b,
                        balancedKd3D,
                        quad3D
                    );
                }
            }
        }

        [Test]
        [Timeout(15000)]
        public void FullBoundsOnHundredGridCountsMatchAcrossTrees()
        {
            Vector2Int size = new(100, 100);
            Vector2[] points = CreateGridPoints(size);
            KdTree2D<Vector2> balancedKd = new(points, p => p, balanced: true);
            KdTree2D<Vector2> unbalancedKd = new(points, p => p, balanced: false);
            QuadTree2D<Vector2> quad = new(points, p => p);

            Bounds fullBounds = new(new Vector3(49.5f, 49.5f, 0f), new Vector3(99f, 99f, 1f));

            List<Vector2> balancedKdResults = new();
            balancedKd.GetElementsInBounds(fullBounds, balancedKdResults);
            List<Vector2> unbalancedKdResults = new();
            unbalancedKd.GetElementsInBounds(fullBounds, unbalancedKdResults);
            List<Vector2> quadResults = new();
            quad.GetElementsInBounds(fullBounds, quadResults);

            Assert.AreEqual(
                balancedKdResults.Count,
                unbalancedKdResults.Count,
                "BalancedKD and UnbalancedKD returned different counts for full bounds: Balanced={0}, Unbalanced={1}",
                balancedKdResults.Count,
                unbalancedKdResults.Count
            );

            Assert.AreEqual(
                balancedKdResults.Count,
                quadResults.Count,
                "BalancedKD and QuadTree returned different counts for full bounds: KD={0}, Quad={1}",
                balancedKdResults.Count,
                quadResults.Count
            );

            Assert.AreEqual(
                10_000,
                balancedKdResults.Count,
                "Expected full dataset bounds to return all elements, but BalancedKD returned {0}.",
                balancedKdResults.Count
            );

            Assert.AreEqual(
                10_000,
                quadResults.Count,
                "Expected full dataset bounds to return all elements, but QuadTree returned {0}.",
                quadResults.Count
            );
        }

        [Test]
        public void VariousCentersAndSizesProduceMatchingResults()
        {
            Vector2Int size = new(16, 16);
            Vector2[] points = CreateGridPoints(size);
            KdTree2D<Vector2> balancedKd = new(points, p => p, balanced: true);
            KdTree2D<Vector2> unbalancedKd = new(points, p => p, balanced: false);
            QuadTree2D<Vector2> quad = new(points, p => p);

            List<(Vector2 Center, Vector2 Size)> cases = new()
            {
                (new Vector2(7.5f, 7.5f), new Vector2(1f, 1f)),
                (new Vector2(0.5f, 7.5f), new Vector2(1f, 1f)),
                (new Vector2(15.5f, 7.5f), new Vector2(1f, 1f)),
                (new Vector2(7.5f, 0.5f), new Vector2(1f, 1f)),
                (new Vector2(7.5f, 15.5f), new Vector2(1f, 1f)),
                (new Vector2(7.5f, 7.5f), new Vector2(3f, 1f)),
                (new Vector2(7.5f, 7.5f), new Vector2(5f, 2f)),
                (new Vector2(6.25f, 8.75f), new Vector2(2f, 2f)),
                (new Vector2(6.25f, 8.75f), new Vector2(1f, 3f)),
            };

            foreach ((Vector2 center, Vector2 sizeVec) in cases)
            {
                Vector2 clampedSize = new(Mathf.Max(sizeVec.x, 1f), Mathf.Max(sizeVec.y, 1f));
                Bounds b = new(center, new Vector3(clampedSize.x, clampedSize.y, 1f));

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
                    "Various centers and sizes mismatch (BalancedKD vs UnbalancedKD)",
                    b,
                    balancedKd3D,
                    unbalancedKd3D
                );

                SpatialDiagnostics.AssertMatchingResults(
                    "Various centers and sizes mismatch (BalancedKD vs QuadTree)",
                    b,
                    balancedKd3D,
                    quad3D
                );
            }
        }

        [Test]
        public void SlidingWindowAlongAxesMatchesAcrossTrees()
        {
            Vector2Int grid = new(12, 12);
            Vector2[] points = CreateGridPoints(grid);
            KdTree2D<Vector2> balancedKd = new(points, p => p, balanced: true);
            KdTree2D<Vector2> unbalancedKd = new(points, p => p, balanced: false);
            QuadTree2D<Vector2> quad = new(points, p => p);

            Vector2 baseSize = new(3f, 3f);
            for (int i = 0; i <= 9; ++i)
            {
                float c = i + 1.5f;
                Bounds bx = new(new Vector3(c, 5.5f, 0f), new Vector3(baseSize.x, baseSize.y, 1f));
                Bounds by = new(new Vector3(5.5f, c, 0f), new Vector3(baseSize.x, baseSize.y, 1f));

                foreach (Bounds b in new[] { bx, by })
                {
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
                        "Sliding window mismatch (BalancedKD vs UnbalancedKD)",
                        b,
                        balancedKd3D,
                        unbalancedKd3D
                    );

                    SpatialDiagnostics.AssertMatchingResults(
                        "Sliding window mismatch (BalancedKD vs QuadTree)",
                        b,
                        balancedKd3D,
                        quad3D
                    );
                }
            }
        }

        [Test]
        public void UnitBoundsAtGridCenterOnTenGridConsistentWithAllTrees()
        {
            Vector2Int size = new(10, 10);
            Vector2[] points = CreateGridPoints(size);
            KdTree2D<Vector2> balancedKd = new(points, p => p, balanced: true);
            KdTree2D<Vector2> unbalancedKd = new(points, p => p, balanced: false);
            QuadTree2D<Vector2> quad = new(points, p => p);

            Bounds b = new(new Vector3(4.5f, 4.5f, 0f), new Vector3(1f, 1f, 1f));
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
                "Unit bounds at grid center mismatch (BalancedKD vs UnbalancedKD)",
                b,
                balancedKd3D,
                unbalancedKd3D
            );

            SpatialDiagnostics.AssertMatchingResults(
                "Unit bounds at grid center mismatch (BalancedKD vs QuadTree)",
                b,
                balancedKd3D,
                quad3D
            );

            Assert.AreEqual(
                4,
                balancedKdResults.Count,
                "Expected BalancedKD to return 4 points for unit bounds at grid center, got {0}.",
                balancedKdResults.Count
            );
        }

        [Test]
        public void EdgeTouchingBoundsConsistentAcrossTrees()
        {
            Vector2Int size = new(10, 10);
            Vector2[] points = CreateGridPoints(size);
            KdTree2D<Vector2> balancedKd = new(points, p => p, balanced: true);
            KdTree2D<Vector2> unbalancedKd = new(points, p => p, balanced: false);
            QuadTree2D<Vector2> quad = new(points, p => p);

            Bounds[] cases =
            {
                new(new Vector3(0.5f, 4.5f, 0f), new Vector3(1f, 1f, 1f)),
                new(new Vector3(9.5f, 4.5f, 0f), new Vector3(1f, 1f, 1f)),
                new(new Vector3(4.5f, 0.5f, 0f), new Vector3(1f, 1f, 1f)),
                new(new Vector3(4.5f, 9.5f, 0f), new Vector3(1f, 1f, 1f)),
            };

            foreach (Bounds b in cases)
            {
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
                    "Edge touching bounds mismatch (BalancedKD vs UnbalancedKD)",
                    b,
                    balancedKd3D,
                    unbalancedKd3D
                );

                SpatialDiagnostics.AssertMatchingResults(
                    "Edge touching bounds mismatch (BalancedKD vs QuadTree)",
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
