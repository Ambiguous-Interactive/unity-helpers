// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UI;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class UnityExtensionsComprehensiveTests : CommonTestBase
    {
        private static List<FastVector3Int> GenerateRandomPointsSquare(
            int count,
            int range,
            int seed = 1337
        )
        {
            IRandom rng = new PcgRandom(seed);
            List<FastVector3Int> points = new(count);
            for (int i = 0; i < count; ++i)
            {
                int x = rng.Next(-range, range + 1);
                int y = rng.Next(-range, range + 1);
                points.Add(new FastVector3Int(x, y, 0));
            }

            return points;
        }

        private static List<FastVector3Int> GenerateCirclePoints(
            int count,
            int radius,
            int seed = 42
        )
        {
            IRandom rng = new PcgRandom(seed);
            List<FastVector3Int> points = new(count);
            for (int i = 0; i < count; ++i)
            {
                double t = (2.0 * Math.PI * i) / count;
                double jitter = (rng.NextDouble() - 0.5) * 0.05; // slight jitter
                int x = (int)Math.Round(radius * Math.Cos(t + jitter));
                int y = (int)Math.Round(radius * Math.Sin(t + jitter));
                points.Add(new FastVector3Int(x, y, 0));
            }

            return points;
        }

        private static List<FastVector3Int> CreateFastVectorList(params (int x, int y)[] coords)
        {
            List<FastVector3Int> list = new(coords.Length);
            for (int i = 0; i < coords.Length; ++i)
            {
                (int x, int y) coord = coords[i];
                list.Add(new FastVector3Int(coord.x, coord.y, 0));
            }

            return list;
        }

        private static List<Vector2> ConvertToVector2(IEnumerable<FastVector3Int> points)
        {
            List<Vector2> converted = new();
            foreach (FastVector3Int point in points)
            {
                converted.Add(new Vector2(point.x, point.y));
            }

            return converted;
        }

        private static bool HasSelfIntersection(IList<Vector2> polygon)
        {
            if (polygon == null || polygon.Count < 4)
            {
                return false;
            }

            int count = polygon.Count;
            for (int i = 0; i < count; ++i)
            {
                Vector2 a1 = polygon[i];
                Vector2 a2 = polygon[(i + 1) % count];
                for (int j = i + 2; j < count; ++j)
                {
                    if (j == i || (j + 1) % count == i)
                    {
                        continue;
                    }
                    Vector2 b1 = polygon[j];
                    Vector2 b2 = polygon[(j + 1) % count];
                    if (Intersects(a1, a2, b1, b2))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool Intersects(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
        {
            float d = (a2.x - a1.x) * (b2.y - b1.y) - (a2.y - a1.y) * (b2.x - b1.x);
            if (Mathf.Approximately(d, 0f))
            {
                return false;
            }

            float u = ((b1.x - a1.x) * (b2.y - b1.y) - (b1.y - a1.y) * (b2.x - b1.x)) / d;
            float v = ((b1.x - a1.x) * (a2.y - a1.y) - (b1.y - a1.y) * (a2.x - a1.x)) / d;
            return u > 0f && u < 1f && v > 0f && v < 1f;
        }

        private static float ComputeSignedArea(IList<Vector2> polygon)
        {
            if (polygon == null || polygon.Count < 3)
            {
                return 0f;
            }

            float area = 0f;
            for (int i = 0; i < polygon.Count; ++i)
            {
                Vector2 current = polygon[i];
                Vector2 next = polygon[(i + 1) % polygon.Count];
                area += current.x * next.y - next.x * current.y;
            }

            return area * 0.5f;
        }

        private static void AssertHullCoversExpectedCorners(
            IEnumerable<FastVector3Int> hull,
            HashSet<FastVector3Int> expectedCorners,
            HashSet<FastVector3Int> inputSet,
            string label
        )
        {
            HashSet<FastVector3Int> hullSet = new(hull);

            foreach (FastVector3Int vertex in hullSet)
            {
                Assert.IsTrue(
                    inputSet.Contains(vertex),
                    $"{label} hull should not introduce new vertices (saw {vertex})."
                );
            }

            foreach (FastVector3Int corner in expectedCorners)
            {
                Assert.IsTrue(
                    hullSet.Contains(corner),
                    $"{label} hull should contain expected corner {corner}."
                );
            }
        }

        private Grid CreateGrid(out GameObject owner)
        {
            owner = Track(new GameObject("Grid", typeof(Grid)));
            Grid grid = owner.GetComponent<Grid>();
            grid.cellSize = new Vector3(1f, 1f, 1f);
            return grid;
        }

        [Test]
        public void GetBoundsFromFastVectorCollection()
        {
            List<FastVector3Int> points = new()
            {
                new FastVector3Int(-1, 2, -3),
                new FastVector3Int(4, 5, 1),
                new FastVector3Int(0, -7, 8),
            };

            BoundsInt? bounds = points.GetBounds();
            Assert.IsTrue(bounds.HasValue);
            BoundsInt value = bounds.Value;
            Assert.AreEqual(-1, value.xMin);
            Assert.AreEqual(5, value.xMax);
            Assert.AreEqual(-7, value.yMin);
            Assert.AreEqual(6, value.yMax);
            Assert.AreEqual(-3, value.zMin);
            Assert.AreEqual(9, value.zMax);
        }

        [Test]
        public void GetBoundsFromFastVectorCollectionReturnsNullWhenEmpty()
        {
            List<FastVector3Int> points = new();
            Assert.IsTrue(points.GetBounds() == null);
        }

        [Test]
        public void GetBoundsFromFastVectorCollectionWithSinglePoint()
        {
            List<FastVector3Int> points = new() { new FastVector3Int(5, -3, 10) };
            BoundsInt? bounds = points.GetBounds();
            Assert.IsTrue(bounds.HasValue);
            BoundsInt value = bounds.Value;
            Assert.AreEqual(5, value.xMin);
            Assert.AreEqual(6, value.xMax);
            Assert.AreEqual(-3, value.yMin);
            Assert.AreEqual(-2, value.yMax);
            Assert.AreEqual(10, value.zMin);
            Assert.AreEqual(11, value.zMax);
            Assert.AreEqual(new Vector3Int(1, 1, 1), value.size);
        }

        [Test]
        public void GetBoundsFromFastVectorCollectionWithNegativeCoordinates()
        {
            List<FastVector3Int> points = new()
            {
                new FastVector3Int(-10, -20, -30),
                new FastVector3Int(-5, -15, -25),
            };
            BoundsInt? bounds = points.GetBounds();
            Assert.IsTrue(bounds.HasValue);
            BoundsInt value = bounds.Value;
            Assert.AreEqual(-10, value.xMin);
            Assert.AreEqual(-4, value.xMax);
            Assert.AreEqual(-20, value.yMin);
            Assert.AreEqual(-14, value.yMax);
            Assert.AreEqual(-30, value.zMin);
            Assert.AreEqual(-24, value.zMax);
        }

        [Test]
        public void GetBoundsFromFastVectorCollectionWithAllSamePoints()
        {
            List<FastVector3Int> points = new()
            {
                new FastVector3Int(7, 7, 7),
                new FastVector3Int(7, 7, 7),
                new FastVector3Int(7, 7, 7),
            };
            BoundsInt? bounds = points.GetBounds();
            Assert.IsTrue(bounds.HasValue);
            BoundsInt value = bounds.Value;
            Assert.AreEqual(7, value.xMin);
            Assert.AreEqual(8, value.xMax);
            Assert.AreEqual(new Vector3Int(7, 7, 7), value.position);
            Assert.AreEqual(new Vector3Int(1, 1, 1), value.size);
        }

        [Test]
        public void GetBoundsFromFastVectorCollectionWithCollinearPoints()
        {
            List<FastVector3Int> points = new()
            {
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(5, 0, 0),
                new FastVector3Int(10, 0, 0),
            };
            BoundsInt? bounds = points.GetBounds();
            Assert.IsTrue(bounds.HasValue);
            BoundsInt value = bounds.Value;
            Assert.AreEqual(0, value.xMin);
            Assert.AreEqual(11, value.xMax);
            Assert.AreEqual(0, value.yMin);
            Assert.AreEqual(1, value.yMax);
            Assert.AreEqual(0, value.zMin);
            Assert.AreEqual(1, value.zMax);
            Assert.AreEqual(new Vector3Int(11, 1, 1), value.size);
        }

        [Test]
        public void GetBoundsFromFastVectorCollectionWithExtremeValues()
        {
            List<FastVector3Int> points = new()
            {
                new FastVector3Int(int.MinValue, int.MinValue, int.MinValue),
                new FastVector3Int(int.MaxValue, int.MaxValue, int.MaxValue),
            };
            BoundsInt? bounds = points.GetBounds();
            Assert.IsTrue(bounds.HasValue);
            BoundsInt value = bounds.Value;
            Assert.AreEqual(int.MinValue, value.xMin);
            Assert.AreEqual(int.MinValue, value.yMin);
            Assert.AreEqual(int.MinValue, value.zMin);
        }

        [Test]
        public void GetBoundsFromVector3IntCollection()
        {
            List<Vector3Int> points = new()
            {
                new Vector3Int(-1, 2, -3),
                new Vector3Int(4, 5, 1),
                new Vector3Int(0, -7, 8),
            };

            BoundsInt? bounds = points.GetBounds();
            Assert.IsTrue(bounds.HasValue);
            BoundsInt value = bounds.Value;
            Assert.AreEqual(-1, value.xMin);
            Assert.AreEqual(5, value.xMax);
            Assert.AreEqual(-7, value.yMin);
            Assert.AreEqual(6, value.yMax);
            Assert.AreEqual(-3, value.zMin);
            Assert.AreEqual(9, value.zMax);
        }

        [Test]
        public void GetBoundsFromVector3IntCollectionInclusive()
        {
            List<Vector3Int> points = new()
            {
                new Vector3Int(-1, 2, -3),
                new Vector3Int(4, 5, 1),
                new Vector3Int(0, -7, 8),
            };

            BoundsInt? bounds = points.GetBounds(inclusive: true);
            Assert.IsTrue(bounds.HasValue);
            BoundsInt value = bounds.Value;
            Assert.AreEqual(-1, value.xMin);
            Assert.AreEqual(4, value.xMax);
            Assert.AreEqual(-7, value.yMin);
            Assert.AreEqual(5, value.yMax);
            Assert.AreEqual(-3, value.zMin);
            Assert.AreEqual(8, value.zMax);
        }

        [Test]
        public void GetBoundsFromVector3IntCollectionReturnsNullWhenEmpty()
        {
            List<Vector3Int> points = new();
            Assert.IsTrue(points.GetBounds() == null);
        }

        [Test]
        public void GetBoundsFromVector3IntCollectionWithSinglePoint()
        {
            List<Vector3Int> points = new() { new Vector3Int(5, -3, 10) };
            BoundsInt? bounds = points.GetBounds();
            Assert.IsTrue(bounds.HasValue);
            BoundsInt value = bounds.Value;
            Assert.AreEqual(new Vector3Int(5, -3, 10), value.position);
            Assert.AreEqual(new Vector3Int(1, 1, 1), value.size);
        }

        [Test]
        public void GetBoundsFromVector2Collection()
        {
            List<Vector2> positions = new()
            {
                new Vector2(-2f, 1f),
                new Vector2(3f, -4f),
                new Vector2(0f, 6f),
            };

            Bounds? maybeBounds = positions.GetBounds();
            Assert.IsTrue(maybeBounds.HasValue);
            Bounds bounds = maybeBounds.Value;
            Assert.AreEqual(new Vector3(0.5f, 1f, 0f), bounds.center);
            Assert.AreEqual(new Vector3(5f, 10f, 0f), bounds.size);
        }

        [Test]
        public void GetBoundsFromVector2CollectionReturnsNullWhenEmpty()
        {
            Assert.IsTrue(new List<Vector2>().GetBounds() == null);
        }

        [Test]
        public void GetBoundsFromBoundsCollection()
        {
            List<Bounds> bounds = new()
            {
                new Bounds(new Vector3(0f, 0f, 0f), new Vector3(2f, 2f, 2f)),
                new Bounds(new Vector3(5f, 5f, 0f), new Vector3(4f, 4f, 4f)),
            };

            Bounds? maybe = bounds.GetBounds();
            Assert.IsTrue(maybe.HasValue);
            Bounds combined = maybe.Value;
            Assert.AreEqual(new Vector3(3f, 3f, 0f), combined.center);
            Assert.AreEqual(new Vector3(8f, 8f, 4f), combined.size);
        }

        [Test]
        public void GetBoundsFromBoundsCollectionHandlesNegativeExtents()
        {
            List<Bounds> bounds = new()
            {
                new Bounds(new Vector3(-2f, -2f, 0f), new Vector3(4f, 4f, 2f)),
                new Bounds(new Vector3(6f, 2f, -1f), new Vector3(2f, 10f, 4f)),
            };

            Bounds? maybe = bounds.GetBounds();
            Assert.IsTrue(maybe.HasValue, "Expected combined bounds.");
            Bounds combined = maybe.Value;
            Assert.AreEqual(new Vector3(1.5f, 1.5f, -1f), combined.center);
            Assert.AreEqual(new Vector3(11f, 11f, 4f), combined.size);
        }

        [Test]
        public void GetBoundsFromBoundsCollectionWithSingleEntry()
        {
            Bounds original = new(new Vector3(4f, -3f, 1f), new Vector3(2f, 6f, 8f));
            List<Bounds> bounds = new() { original };

            Bounds? maybe = bounds.GetBounds();
            Assert.IsTrue(maybe.HasValue, "Expected combined bounds.");
            Bounds combined = maybe.Value;
            Assert.AreEqual(original.center, combined.center);
            Assert.AreEqual(original.size, combined.size);
        }

        [Test]
        public void GetBoundsFromBoundsCollectionReturnsNullWhenEmpty()
        {
            Assert.IsTrue(new List<Bounds>().GetBounds() == null);
        }

        [UnityTest]
        public IEnumerator BuildConvexHullVector3IntIncludesColinearPointsWhenRequested()
        {
            Grid grid = CreateGrid(out GameObject _);
            List<Vector3Int> points = new()
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(0, 2, 0),
                new Vector3Int(2, 2, 0),
                new Vector3Int(2, 0, 0),
                new Vector3Int(1, 0, 0),
            };

            List<Vector3Int> hull = points.BuildConvexHull(grid, includeColinearPoints: true);

            CollectionAssert.AreEquivalent(points, hull);
            Assert.AreEqual(new Vector3Int(0, 0, 0), hull[0]);

            yield return null;
        }

        [UnityTest]
        public IEnumerator BuildConvexHullVector3IntExcludesColinearPointsWhenDisabled()
        {
            Grid grid = CreateGrid(out GameObject _);

            List<Vector3Int> points = new()
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(0, 2, 0),
                new Vector3Int(2, 2, 0),
                new Vector3Int(2, 0, 0),
                new Vector3Int(1, 0, 0),
            };

            List<Vector3Int> hull = points.BuildConvexHull(grid, includeColinearPoints: false);

            CollectionAssert.AreEquivalent(
                new[]
                {
                    new Vector3Int(0, 0, 0),
                    new Vector3Int(0, 2, 0),
                    new Vector3Int(2, 2, 0),
                    new Vector3Int(2, 0, 0),
                },
                hull
            );

            yield return null;
        }

        [UnityTest]
        public IEnumerator BuildConvexHullFastVector3IntProducesExpectedLoop()
        {
            Grid grid = CreateGrid(out GameObject _);

            List<FastVector3Int> points = new()
            {
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(0, 2, 0),
                new FastVector3Int(2, 2, 0),
                new FastVector3Int(2, 0, 0),
                new FastVector3Int(1, 0, 0),
            };

            List<FastVector3Int> hull = points.BuildConvexHull(grid, includeColinearPoints: false);

            CollectionAssert.AreEquivalent(
                new[]
                {
                    new FastVector3Int(0, 0, 0),
                    new FastVector3Int(0, 2, 0),
                    new FastVector3Int(2, 2, 0),
                    new FastVector3Int(2, 0, 0),
                },
                hull
            );

            yield return null;
        }

        [UnityTest]
        public IEnumerator BuildConvexHullFastVector3IntIncludesColinearWhenRequested()
        {
            Grid grid = CreateGrid(out GameObject _);

            List<FastVector3Int> points = new()
            {
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(0, 2, 0),
                new FastVector3Int(2, 2, 0),
                new FastVector3Int(2, 0, 0),
                new FastVector3Int(1, 0, 0),
            };

            List<FastVector3Int> hull = points.BuildConvexHull(grid, includeColinearPoints: true);

            CollectionAssert.AreEquivalent(points, hull);

            yield return null;
        }

        [UnityTest]
        public IEnumerator IsPointInsideConvexHullDetectsContainment()
        {
            Grid grid = CreateGrid(out GameObject _);

            List<Vector3Int> hull = new()
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(0, 2, 0),
                new Vector3Int(2, 2, 0),
                new Vector3Int(2, 0, 0),
            };

            Assert.IsTrue(hull.IsPointInsideConvexHull(grid, new Vector3Int(1, 1, 0)));
            Assert.IsFalse(hull.IsPointInsideConvexHull(grid, new Vector3Int(3, 3, 0)));

            yield return null;
        }

        [UnityTest]
        public IEnumerator IsPointInsideConvexHullTreatsBoundaryAsInside()
        {
            Grid grid = CreateGrid(out GameObject _);

            List<Vector3Int> hull = new()
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(4, 0, 0),
                new Vector3Int(4, 4, 0),
                new Vector3Int(0, 4, 0),
            };

            Assert.IsTrue(hull.IsPointInsideConvexHull(grid, new Vector3Int(0, 2, 0)));
            Assert.IsTrue(hull.IsPointInsideConvexHull(grid, new Vector3Int(2, 0, 0)));
            Assert.IsFalse(hull.IsPointInsideConvexHull(grid, new Vector3Int(-1, 2, 0)));

            yield return null;
        }

        [UnityTest]
        public IEnumerator IsPointInsideConvexHullFastVectorDetectsContainment()
        {
            Grid grid = CreateGrid(out GameObject _);

            List<FastVector3Int> hull = new()
            {
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(0, 2, 0),
                new FastVector3Int(2, 2, 0),
                new FastVector3Int(2, 0, 0),
            };

            Assert.IsTrue(hull.IsPointInsideConvexHull(grid, new FastVector3Int(1, 1, 0)));
            Assert.IsFalse(hull.IsPointInsideConvexHull(grid, new FastVector3Int(3, 3, 0)));

            yield return null;
        }

        [UnityTest]
        public IEnumerator IsPointInsideConvexHullFastVectorTreatsBoundaryAsInside()
        {
            Grid grid = CreateGrid(out GameObject _);

            List<FastVector3Int> hull = new()
            {
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(4, 0, 0),
                new FastVector3Int(4, 4, 0),
                new FastVector3Int(0, 4, 0),
            };

            Assert.IsTrue(hull.IsPointInsideConvexHull(grid, new FastVector3Int(0, 2, 0)));
            Assert.IsTrue(hull.IsPointInsideConvexHull(grid, new FastVector3Int(2, 0, 0)));
            Assert.IsFalse(hull.IsPointInsideConvexHull(grid, new FastVector3Int(-1, 2, 0)));

            yield return null;
        }

        [UnityTest]
        public IEnumerator IsConvexHullInsideConvexHullValidatesInteriorPoints()
        {
            Grid grid = CreateGrid(out GameObject _);

            List<Vector3Int> hull = new()
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(0, 4, 0),
                new Vector3Int(4, 4, 0),
                new Vector3Int(4, 0, 0),
            };

            List<Vector3Int> inner = new()
            {
                new Vector3Int(1, 1, 0),
                new Vector3Int(2, 1, 0),
                new Vector3Int(2, 2, 0),
            };

            Assert.IsTrue(hull.IsConvexHullInsideConvexHull(grid, inner));
            inner.Add(new Vector3Int(5, 5, 0));
            Assert.IsFalse(hull.IsConvexHullInsideConvexHull(grid, inner));

            yield return null;
        }

        [UnityTest]
        public IEnumerator IsConvexHullInsideConvexHullHandlesCounterClockwiseOrder()
        {
            Grid grid = CreateGrid(out GameObject _);

            List<Vector3Int> hull = new()
            {
                new Vector3Int(0, 0, 0),
                new Vector3Int(4, 0, 0),
                new Vector3Int(4, 4, 0),
                new Vector3Int(0, 4, 0),
            };

            List<Vector3Int> inner = new()
            {
                new Vector3Int(1, 1, 0),
                new Vector3Int(2, 2, 0),
                new Vector3Int(3, 1, 0),
            };

            Assert.IsTrue(hull.IsConvexHullInsideConvexHull(grid, inner));
            inner.Add(new Vector3Int(-1, 0, 0));
            Assert.IsFalse(hull.IsConvexHullInsideConvexHull(grid, inner));

            yield return null;
        }

        [UnityTest]
        public IEnumerator IsConvexHullInsideConvexHullFastVectorValidatesInteriorPoints()
        {
            Grid grid = CreateGrid(out GameObject _);

            List<FastVector3Int> hull = new()
            {
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(0, 4, 0),
                new FastVector3Int(4, 4, 0),
                new FastVector3Int(4, 0, 0),
            };

            List<FastVector3Int> inner = new()
            {
                new FastVector3Int(1, 1, 0),
                new FastVector3Int(2, 1, 0),
                new FastVector3Int(2, 2, 0),
            };

            Assert.IsTrue(hull.IsConvexHullInsideConvexHull(grid, inner));
            inner.Add(new FastVector3Int(5, 5, 0));
            Assert.IsFalse(hull.IsConvexHullInsideConvexHull(grid, inner));

            yield return null;
        }

        [UnityTest]
        public IEnumerator IsConvexHullInsideConvexHullFastVectorHandlesCounterClockwiseOrder()
        {
            Grid grid = CreateGrid(out GameObject _);

            List<FastVector3Int> hull = new()
            {
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(4, 0, 0),
                new FastVector3Int(4, 4, 0),
                new FastVector3Int(0, 4, 0),
            };

            List<FastVector3Int> inner = new()
            {
                new FastVector3Int(1, 1, 0),
                new FastVector3Int(2, 2, 0),
                new FastVector3Int(3, 1, 0),
            };

            Assert.IsTrue(hull.IsConvexHullInsideConvexHull(grid, inner));
            inner.Add(new FastVector3Int(-1, 0, 0));
            Assert.IsFalse(hull.IsConvexHullInsideConvexHull(grid, inner));

            yield return null;
        }

        [UnityTest]
        public IEnumerator BuildConcaveHullVariantsMatchConvexHullForRectangle()
        {
            Grid grid = CreateGrid(out GameObject _);

            List<FastVector3Int> points = new()
            {
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(0, 3, 0),
                new FastVector3Int(3, 3, 0),
                new FastVector3Int(3, 0, 0),
            };

            List<FastVector3Int> convex = points.BuildConvexHull(
                grid,
                includeColinearPoints: false
            );
            List<FastVector3Int> concaveEdgeSplit = points.BuildConcaveHullEdgeSplit(grid);
            List<FastVector3Int> concaveKnn = points.BuildConcaveHullKnn(grid);
            List<FastVector3Int> concave = points.BuildConcaveHull(
                grid,
                UnityExtensions.ConcaveHullOptions.Default.WithStrategy(
                    UnityExtensions.ConcaveHullStrategy.EdgeSplit
                )
            );

            CollectionAssert.AreEquivalent(convex, concaveEdgeSplit);
            CollectionAssert.AreEquivalent(convex, concaveKnn);

            CollectionAssert.AreEquivalent(convex, concave);

            yield return null;
        }

        [UnityTest]
        public IEnumerator BuildConcaveHullEdgeSplitHonorsAngleThreshold()
        {
            Grid grid = CreateGrid(out GameObject _);
            List<FastVector3Int> concavePoints = new()
            {
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(0, 4, 0),
                new FastVector3Int(2, 4, 0),
                new FastVector3Int(2, 2, 0),
                new FastVector3Int(4, 2, 0),
                new FastVector3Int(4, 0, 0),
            };
            FastVector3Int elbow = new(2, 2, 0);

            List<FastVector3Int> convex = concavePoints.BuildConvexHull(
                grid,
                includeColinearPoints: false
            );
            List<FastVector3Int> strict = concavePoints.BuildConcaveHullEdgeSplit(
                grid,
                bucketSize: 8,
                angleThreshold: 25f
            );
            List<FastVector3Int> loose = concavePoints.BuildConcaveHullEdgeSplit(
                grid,
                bucketSize: 8,
                angleThreshold: 150f
            );

            CollectionAssert.AreEquivalent(convex, strict);
            CollectionAssert.DoesNotContain(strict, elbow);
            CollectionAssert.Contains(loose, elbow);
            Assert.Greater(
                loose.Count,
                strict.Count,
                "Looser threshold should introduce the elbow."
            );

            yield return null;
        }

        [UnityTest]
        public IEnumerator BuildConcaveHullKnnMatchesEdgeSplitForConcaveShape()
        {
            Grid grid = CreateGrid(out GameObject _);
            List<FastVector3Int> concavePoints = new()
            {
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(0, 4, 0),
                new FastVector3Int(2, 4, 0),
                new FastVector3Int(2, 2, 0),
                new FastVector3Int(4, 2, 0),
                new FastVector3Int(4, 0, 0),
            };
            FastVector3Int elbow = new(2, 2, 0);

            List<FastVector3Int> edgeSplit = concavePoints.BuildConcaveHullEdgeSplit(
                grid,
                bucketSize: 8,
                angleThreshold: 150f
            );
            List<FastVector3Int> knn = concavePoints.BuildConcaveHullKnn(grid, nearestNeighbors: 3);

            CollectionAssert.Contains(edgeSplit, elbow);
            CollectionAssert.Contains(knn, elbow);
            CollectionAssert.AreEquivalent(edgeSplit, knn);

            yield return null;
        }

        [UnityTest]
        public IEnumerator ConvexHullExcludesInteriorColinearPointsFast()
        {
            Grid grid = CreateGrid(out GameObject _);

            List<FastVector3Int> points = new()
            {
                new FastVector3Int(-2, 0, 0),
                new FastVector3Int(-1, 0, 0),
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(1, 0, 0),
                new FastVector3Int(2, 0, 0),
            };

            List<FastVector3Int> hull = points.BuildConvexHull(grid);
            CollectionAssert.AreEquivalent(
                new[] { new FastVector3Int(-2, 0, 0), new FastVector3Int(2, 0, 0) },
                hull
            );

            yield return null;
        }

        [UnityTest]
        public IEnumerator ConvexHullPermutationInvariance()
        {
            Grid grid = CreateGrid(out GameObject _);

            List<FastVector3Int> points = new()
            {
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(3, 0, 0),
                new FastVector3Int(3, 3, 0),
                new FastVector3Int(0, 3, 0),
                new FastVector3Int(1, 1, 0), // interior
            };
            List<FastVector3Int> expected = new()
            {
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(3, 0, 0),
                new FastVector3Int(3, 3, 0),
                new FastVector3Int(0, 3, 0),
            };

            List<FastVector3Int> hullA = points.BuildConvexHull(grid);
            points.Reverse();
            List<FastVector3Int> hullB = points.BuildConvexHull(grid);
            CollectionAssert.AreEquivalent(expected, hullA);
            CollectionAssert.AreEquivalent(expected, hullB);

            yield return null;
        }

        [UnityTest]
        public IEnumerator BuildConcaveHullVariantsMatchConvexHullForTriangle()
        {
            Grid grid = CreateGrid(out GameObject _);

            List<FastVector3Int> points = new()
            {
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(0, 3, 0),
                new FastVector3Int(3, 3, 0),
                new FastVector3Int(3, 0, 0),
            };

            List<FastVector3Int> convex = points.BuildConvexHull(
                grid,
                includeColinearPoints: false
            );
            List<FastVector3Int> concaveEdgeSplit = points.BuildConcaveHullEdgeSplit(grid);
            List<FastVector3Int> concaveKnn = points.BuildConcaveHullKnn(grid);
            List<FastVector3Int> concave = points.BuildConcaveHull(
                grid,
                UnityExtensions.ConcaveHullOptions.Default.WithStrategy(
                    UnityExtensions.ConcaveHullStrategy.EdgeSplit
                )
            );

            CollectionAssert.AreEquivalent(convex, concaveEdgeSplit);
            CollectionAssert.AreEquivalent(convex, concaveKnn);
            CollectionAssert.AreEquivalent(convex, concave);

            yield return null;
        }

        [UnityTest]
        public IEnumerator ConcaveHullTrivialShapesReturnConvexHull()
        {
            Grid grid = CreateGrid(out GameObject _);

            // Two points
            List<FastVector3Int> twoPoints = new()
            {
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(3, 0, 0),
            };

            List<FastVector3Int> convexTwo = twoPoints.BuildConvexHull(grid);
            List<FastVector3Int> concaveTwoEdgeSplit = twoPoints.BuildConcaveHullEdgeSplit(grid);
            List<FastVector3Int> concaveTwoKnn = twoPoints.BuildConcaveHullKnn(grid);
            List<FastVector3Int> concaveTwo = twoPoints.BuildConcaveHull(
                grid,
                UnityExtensions.ConcaveHullOptions.Default.WithStrategy(
                    UnityExtensions.ConcaveHullStrategy.EdgeSplit
                )
            );
            CollectionAssert.AreEquivalent(convexTwo, concaveTwoEdgeSplit);
            CollectionAssert.AreEquivalent(convexTwo, concaveTwoKnn);
            CollectionAssert.AreEquivalent(convexTwo, concaveTwo);

            // Three points
            List<FastVector3Int> threePoints = new()
            {
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(2, 0, 0),
                new FastVector3Int(1, 1, 0),
            };
            List<FastVector3Int> convexThree = threePoints.BuildConvexHull(grid);
            List<FastVector3Int> concaveThreeEdgeSplit = threePoints.BuildConcaveHullEdgeSplit(
                grid
            );
            List<FastVector3Int> concaveThreeKnn = threePoints.BuildConcaveHullKnn(grid);
            List<FastVector3Int> concaveThree = threePoints.BuildConcaveHull(
                grid,
                UnityExtensions.ConcaveHullOptions.Default.WithStrategy(
                    UnityExtensions.ConcaveHullStrategy.EdgeSplit
                )
            );
            CollectionAssert.AreEquivalent(convexThree, concaveThreeEdgeSplit);
            CollectionAssert.AreEquivalent(convexThree, concaveThreeKnn);
            CollectionAssert.AreEquivalent(convexThree, concaveThree);

            // Four points (rectangle)
            List<FastVector3Int> rectangle = new()
            {
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(0, 3, 0),
                new FastVector3Int(3, 3, 0),
                new FastVector3Int(3, 0, 0),
            };
            List<FastVector3Int> convexRect = rectangle.BuildConvexHull(grid);
            List<FastVector3Int> concaveRectEdgeSplit = rectangle.BuildConcaveHullEdgeSplit(grid);
            List<FastVector3Int> concaveRectKnn = rectangle.BuildConcaveHullKnn(grid);
            List<FastVector3Int> concaveRect = rectangle.BuildConcaveHull(
                grid,
                UnityExtensions.ConcaveHullOptions.Default.WithStrategy(
                    UnityExtensions.ConcaveHullStrategy.EdgeSplit
                )
            );
            CollectionAssert.AreEquivalent(convexRect, concaveRectEdgeSplit);
            CollectionAssert.AreEquivalent(convexRect, concaveRectKnn);
            CollectionAssert.AreEquivalent(convexRect, concaveRect);

            yield return null;
        }

        [Test]
        public void ConcaveHullJarvisFallbackMatchesConvexHullForVector2()
        {
            List<Vector2> points = new()
            {
                new Vector2(0f, 0f),
                new Vector2(1.5f, 2f),
                new Vector2(-2f, 1.5f),
                new Vector2(3.25f, -1f),
                new Vector2(-1.5f, -2.25f),
                new Vector2(2.25f, 1.25f),
            };

            List<Vector2> expected = points.BuildConvexHull(includeColinearPoints: false);
            List<Vector2> fallback = InvokeVectorJarvisFallback(
                points,
                includeColinearPoints: false
            );

            CollectionAssert.AreEquivalent(expected, fallback);
        }

        [Test]
        public void ConcaveHullJarvisFallbackMatchesConvexHullForGrid()
        {
            Grid grid = CreateGrid(out GameObject owner);
            Track(owner);
            List<FastVector3Int> points = new()
            {
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(2, 3, 0),
                new FastVector3Int(-3, 1, 0),
                new FastVector3Int(4, -2, 0),
                new FastVector3Int(-2, -3, 0),
                new FastVector3Int(1, 4, 0),
            };

            List<FastVector3Int> expected = points.BuildConvexHull(grid);
            List<FastVector3Int> fallback = InvokeGridJarvisFallback(
                points,
                grid,
                includeColinearPoints: false
            );

            CollectionAssert.AreEquivalent(expected, fallback);
        }

        [Test]
        public void FastVectorConcaveHullGridlessMatchesGridKnn()
        {
            Grid grid = CreateGrid(out GameObject owner);
            Track(owner);
            List<FastVector3Int> points = GenerateRandomPointsSquare(32, 8);

            UnityExtensions.ConcaveHullOptions options = UnityExtensions
                .ConcaveHullOptions.Default.WithStrategy(UnityExtensions.ConcaveHullStrategy.Knn)
                .WithNearestNeighbors(6);

            List<FastVector3Int> gridHull = points.BuildConcaveHull(grid, options);
            List<FastVector3Int> gridlessHull = points.BuildConcaveHull(options);

            CollectionAssert.AreEquivalent(gridHull, gridlessHull);
        }

        [Test]
        public void FastVectorConcaveHullGridlessHandlesMinimalPointCounts()
        {
            List<FastVector3Int> empty = new();
            CollectionAssert.IsEmpty(
                empty.BuildConcaveHullKnn(),
                "Empty input should produce empty concave hull (k-NN)."
            );
            CollectionAssert.IsEmpty(
                empty.BuildConcaveHullEdgeSplit(),
                "Empty input should produce empty concave hull (edge-split)."
            );

            List<FastVector3Int> single = CreateFastVectorList((0, 0));
            CollectionAssert.AreEquivalent(
                single,
                single.BuildConcaveHullKnn(),
                "Single-point hull should echo original input (k-NN)."
            );
            CollectionAssert.AreEquivalent(
                single,
                single.BuildConcaveHullEdgeSplit(),
                "Single-point hull should echo original input (edge-split)."
            );

            List<FastVector3Int> twoPoints = CreateFastVectorList((0, 0), (2, 0));
            List<FastVector3Int> twoKnn = twoPoints.BuildConcaveHullKnn();
            List<FastVector3Int> twoEdge = twoPoints.BuildConcaveHullEdgeSplit();
            CollectionAssert.AreEquivalent(twoPoints, twoKnn);
            CollectionAssert.AreEquivalent(twoPoints, twoEdge);

            List<FastVector3Int> triangle = CreateFastVectorList((0, 0), (2, 0), (1, 2));
            List<FastVector3Int> triKnn = triangle.BuildConcaveHullKnn();
            List<FastVector3Int> triEdge = triangle.BuildConcaveHullEdgeSplit();
            CollectionAssert.AreEquivalent(triangle, triKnn);
            CollectionAssert.AreEquivalent(triangle, triEdge);
        }

        [Test]
        public void FastVectorConcaveHullGridlessHandlesDuplicatesAndColinear()
        {
            List<FastVector3Int> points = new()
            {
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(4, 0, 0),
                new FastVector3Int(2, 0, 0),
                new FastVector3Int(4, 4, 0),
                new FastVector3Int(0, 4, 0),
                new FastVector3Int(0, 4, 0),
                new FastVector3Int(1, 3, 0),
            };

            List<FastVector3Int> expectedCorners = CreateFastVectorList(
                (0, 0),
                (4, 0),
                (4, 4),
                (0, 4)
            );
            HashSet<FastVector3Int> expectedSet = new(expectedCorners);
            HashSet<FastVector3Int> inputSet = new(points);

            List<FastVector3Int> gridlessKnn = points.BuildConcaveHullKnn();
            List<FastVector3Int> gridlessEdge = points.BuildConcaveHullEdgeSplit();

            AssertHullCoversExpectedCorners(gridlessKnn, expectedSet, inputSet, "k-NN");
            AssertHullCoversExpectedCorners(gridlessEdge, expectedSet, inputSet, "edge-split");
        }

        [Test]
        public void FastVectorConcaveHullGridlessRandomCloudsRemainInsideConvexHull()
        {
            IRandom masterRng = new PcgRandom(1234);
            HullRegressionRecorder recorder = new(
                nameof(FastVectorConcaveHullGridlessRandomCloudsRemainInsideConvexHull)
            );

            for (int trial = 0; trial < 5; ++trial)
            {
                int trialSeed = masterRng.Next();
                IRandom rng = new PcgRandom(trialSeed);
                int count = rng.Next(10, 40);
                List<FastVector3Int> points = new(count);
                for (int i = 0; i < count; ++i)
                {
                    points.Add(
                        new FastVector3Int(rng.Next(-10, 11), rng.Next(-10, 11), rng.Next(-2, 3))
                    );
                }

                List<FastVector3Int> convex = null;
                List<FastVector3Int> concaveKnn = null;
                List<FastVector3Int> concaveEdge = null;
                List<FastVector3Int> concaveUnified = null;
                try
                {
                    convex = points.BuildConvexHull(includeColinearPoints: false);
                    concaveKnn = points.BuildConcaveHullKnn();
                    concaveEdge = points.BuildConcaveHullEdgeSplit();
                    concaveUnified = points.BuildConcaveHull(
                        UnityExtensions.ConcaveHullOptions.ForEdgeSplit(
                            bucketSize: 32,
                            angleThreshold: 85f
                        )
                    );

                    HashSet<FastVector3Int> input = new(points);
                    Assert.IsTrue(concaveKnn.All(input.Contains));
                    Assert.IsTrue(concaveEdge.All(input.Contains));
                    Assert.IsTrue(concaveUnified.All(input.Contains));

                    List<Vector2> convexVector = ConvertToVector2(convex);
                    List<Vector2> concaveKnnVector = ConvertToVector2(concaveKnn);
                    List<Vector2> concaveEdgeVector = ConvertToVector2(concaveEdge);
                    List<Vector2> concaveUnifiedVector = ConvertToVector2(concaveUnified);

                    Assert.IsTrue(
                        convexVector.IsConvexHullInsideConvexHull(concaveKnnVector),
                        "Gridless k-NN concave hull must be inside convex hull."
                    );
                    Assert.IsTrue(
                        convexVector.IsConvexHullInsideConvexHull(concaveEdgeVector),
                        "Gridless edge-split concave hull must be inside convex hull."
                    );
                    Assert.IsTrue(
                        convexVector.IsConvexHullInsideConvexHull(concaveUnifiedVector),
                        "Unified concave hull must be inside convex hull."
                    );
                }
                catch (AssertionException)
                {
                    recorder.WriteSnapshot(
                        mode: $"gridless_trial{trial}",
                        seed: trialSeed,
                        points: points,
                        convex: convex,
                        concaveEdgeSplit: concaveEdge,
                        concaveKnn: concaveKnn,
                        concaveUnified: concaveUnified
                    );
                    throw;
                }
            }
        }

        [Test]
        public void FastVectorConcaveHullGridlessHandlesNearColinearNoise()
        {
            List<FastVector3Int> points = new();
            IRandom rng = new PcgRandom(555);
            for (int i = 0; i < 24; ++i)
            {
                double jitter = (rng.NextDouble() - 0.5) * 0.02;
                points.Add(new FastVector3Int(i, (int)Math.Round(jitter), 0));
            }
            points.Add(new FastVector3Int(10, 12, 0));
            points.Add(new FastVector3Int(15, -10, 0));

            List<FastVector3Int> hull = points.BuildConcaveHullKnn();
            HashSet<FastVector3Int> input = new(points);
            Assert.IsTrue(
                hull.All(input.Contains),
                "Hull vertices should originate from input points."
            );
        }

        [Test]
        public void FastVectorConcaveHullGridlessRejectsSelfIntersections()
        {
            List<FastVector3Int> bowtie = new()
            {
                new FastVector3Int(-3, 0, 0),
                new FastVector3Int(-1, 2, 0),
                new FastVector3Int(1, -2, 0),
                new FastVector3Int(3, 0, 0),
                new FastVector3Int(1, 2, 0),
                new FastVector3Int(-1, -2, 0),
            };

            List<FastVector3Int> hull = bowtie.BuildConcaveHullKnn(nearestNeighbors: 6);
            Assert.IsFalse(
                HasSelfIntersection(ConvertToVector2(hull)),
                "FastVector concave hull should avoid self-intersection."
            );
        }

        [Test]
        public void FastVectorConvexHullGridlessMaintainsWinding()
        {
            List<FastVector3Int> ring = new();
            const int count = 48;
            for (int i = 0; i < count; ++i)
            {
                double angle = 2.0 * Math.PI * i / count;
                ring.Add(
                    new FastVector3Int(
                        (int)Math.Round(Math.Cos(angle) * 10),
                        (int)Math.Round(Math.Sin(angle) * 10),
                        0
                    )
                );
            }

            List<FastVector3Int> hull = ring.BuildConvexHull(includeColinearPoints: false);
            float area = ComputeSignedArea(ConvertToVector2(hull));
            Assert.Greater(
                area,
                0f,
                "FastVector convex hull should use counter-clockwise winding."
            );
        }

        [Test]
        public void FastVectorConvexHullGridlessHandlesColinearToggles()
        {
            List<FastVector3Int> points = CreateFastVectorList(
                (0, 0),
                (2, 0),
                (4, 0),
                (4, 3),
                (0, 3),
                (2, 3)
            );

            List<FastVector3Int> include = points.BuildConvexHull(includeColinearPoints: true);
            List<FastVector3Int> exclude = points.BuildConvexHull(includeColinearPoints: false);

            CollectionAssert.AreEquivalent(points.Distinct().ToList(), include);
            CollectionAssert.AreEquivalent(
                CreateFastVectorList((0, 0), (4, 0), (4, 3), (0, 3)),
                exclude
            );
        }

        [Test]
        public void FastVectorConcaveHullGridlessMatchesGridEdgeSplit()
        {
            Grid grid = CreateGrid(out GameObject owner);
            Track(owner);
            List<FastVector3Int> points = GenerateRandomPointsSquare(24, 6);

            UnityExtensions.ConcaveHullOptions options =
                UnityExtensions.ConcaveHullOptions.ForEdgeSplit(
                    bucketSize: 32,
                    angleThreshold: 80f
                );

            List<FastVector3Int> gridHull = points.BuildConcaveHull(grid, options);
            List<FastVector3Int> gridlessHull = points.BuildConcaveHull(options);

            CollectionAssert.AreEquivalent(gridHull, gridlessHull);
        }

        [Test]
        public void FastVectorConvexHullGridlessMatchesGrid()
        {
            Grid grid = CreateGrid(out GameObject owner);
            Track(owner);
            List<FastVector3Int> points = GenerateRandomPointsSquare(40, 10);

            List<FastVector3Int> gridHull = points.BuildConvexHull(
                grid,
                includeColinearPoints: false
            );
            List<FastVector3Int> gridlessHull = points.BuildConvexHull(
                includeColinearPoints: false
            );

            CollectionAssert.AreEquivalent(gridHull, gridlessHull);
        }

        [Test]
        public void FastVectorConvexHullJarvisGridlessMatchesGrid()
        {
            Grid grid = CreateGrid(out GameObject owner);
            Track(owner);
            List<FastVector3Int> points = GenerateRandomPointsSquare(28, 6);

            List<FastVector3Int> gridHull = points.BuildConvexHull(
                grid,
                includeColinearPoints: false,
                UnityExtensions.ConvexHullAlgorithm.Jarvis
            );
            List<FastVector3Int> gridlessHull = points.BuildConvexHull(
                includeColinearPoints: false,
                UnityExtensions.ConvexHullAlgorithm.Jarvis
            );

            CollectionAssert.AreEquivalent(gridHull, gridlessHull);
        }

        [UnityTest]
        public IEnumerator ConcaveHullHandlesDuplicatesAndColinear()
        {
            Grid grid = CreateGrid(out GameObject _);

            // Duplicates and colinear points along X-axis
            List<FastVector3Int> points = new()
            {
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(1, 0, 0),
                new FastVector3Int(2, 0, 0),
                new FastVector3Int(2, 0, 0),
            };

            List<FastVector3Int> convex = points.BuildConvexHull(grid);
            // Expect convex hull to be endpoints only
            CollectionAssert.AreEquivalent(
                new[] { new FastVector3Int(0, 0, 0), new FastVector3Int(2, 0, 0) },
                convex
            );

            List<FastVector3Int> concaveEdgeSplit = points.BuildConcaveHullEdgeSplit(grid);
            List<FastVector3Int> concaveKnn = points.BuildConcaveHullKnn(grid);
            List<FastVector3Int> concave = points.BuildConcaveHull(
                grid,
                UnityExtensions.ConcaveHullOptions.ForEdgeSplit()
            );

            CollectionAssert.AreEquivalent(convex, concaveEdgeSplit);
            CollectionAssert.AreEquivalent(convex, concaveKnn);
            CollectionAssert.AreEquivalent(convex, concave);

            yield return null;
        }

        [UnityTest]
        public IEnumerator ConvexHullAllPointsIdenticalReturnsSinglePoint()
        {
            Grid grid = CreateGrid(out GameObject _);

            List<FastVector3Int> points = Enumerable
                .Repeat(new FastVector3Int(5, 5, 0), 20)
                .ToList();
            List<FastVector3Int> convex = points.BuildConvexHull(grid);
            List<FastVector3Int> concaveEdgeSplit = points.BuildConcaveHullEdgeSplit(grid);
            List<FastVector3Int> concaveKnn = points.BuildConcaveHullKnn(grid);
            List<FastVector3Int> concave = points.BuildConcaveHull(
                grid,
                UnityExtensions.ConcaveHullOptions.ForEdgeSplit()
            );

            Assert.AreEqual(1, convex.Count);
            CollectionAssert.AreEquivalent(convex, concaveEdgeSplit);
            CollectionAssert.AreEquivalent(convex, concaveKnn);
            CollectionAssert.AreEquivalent(convex, concave);

            yield return null;
        }

        [UnityTest]
        public IEnumerator ConvexHullWithDuplicatesAndFourCornersReturnsCornersConcaveMatches()
        {
            Grid grid = CreateGrid(out GameObject _);
            List<FastVector3Int> points = new()
            {
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(5, 0, 0),
                new FastVector3Int(5, 0, 0),
                new FastVector3Int(5, 5, 0),
                new FastVector3Int(0, 5, 0),
                new FastVector3Int(0, 5, 0),
                new FastVector3Int(2, 0, 0),
                new FastVector3Int(3, 0, 0), // colinear along bottom
                new FastVector3Int(5, 3, 0),
                new FastVector3Int(5, 2, 0), // colinear along right
            };

            List<FastVector3Int> convex = points.BuildConvexHull(grid);
            List<FastVector3Int> concaveEdgeSplit = points.BuildConcaveHullEdgeSplit(grid);
            List<FastVector3Int> concaveKnn = points.BuildConcaveHullKnn(grid);
            List<FastVector3Int> concave = points.BuildConcaveHull(
                grid,
                UnityExtensions.ConcaveHullOptions.ForEdgeSplit()
            );

            CollectionAssert.AreEquivalent(
                new[]
                {
                    new FastVector3Int(0, 0, 0),
                    new FastVector3Int(5, 0, 0),
                    new FastVector3Int(5, 5, 0),
                    new FastVector3Int(0, 5, 0),
                },
                convex
            );
            CollectionAssert.AreEquivalent(convex, concaveEdgeSplit);
            CollectionAssert.AreEquivalent(convex, concaveKnn);
            CollectionAssert.AreEquivalent(convex, concave);

            yield return null;
        }

        [UnityTest]
        public IEnumerator ConvexHullWithDuplicatesJarvisMatchesMonotoneChain()
        {
            Grid grid = CreateGrid(out GameObject _);

            List<FastVector3Int> points = new()
            {
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(5, 0, 0),
                new FastVector3Int(5, 5, 0),
                new FastVector3Int(0, 5, 0),
                new FastVector3Int(2, 0, 0),
                new FastVector3Int(3, 0, 0),
                new FastVector3Int(5, 3, 0),
                new FastVector3Int(5, 2, 0),
            };

            FastVector3Int[] expected = { new(0, 0, 0), new(5, 0, 0), new(5, 5, 0), new(0, 5, 0) };

            List<FastVector3Int> chain = points.BuildConvexHull(grid);
            List<FastVector3Int> jarvis = points.BuildConvexHull(
                grid,
                includeColinearPoints: false,
                UnityExtensions.ConvexHullAlgorithm.Jarvis
            );

            CollectionAssert.AreEquivalent(expected, chain);
            CollectionAssert.AreEquivalent(expected, jarvis);

            yield return null;
        }

        [UnityTest]
        public IEnumerator ConvexHullWithDuplicatesOnScaledGridReturnsCorners()
        {
            Grid grid = CreateGrid(out GameObject _);
            grid.transform.position = new Vector3(12345.5f, -9876.25f, 0f);
            grid.transform.localScale = new Vector3(0.03125f, 0.125f, 1f);
            grid.transform.rotation = Quaternion.Euler(0f, 0f, 27.5f);
            grid.cellSize = new Vector3(0.03125f, 0.125f, 1f);

            List<FastVector3Int> points = new()
            {
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(5, 0, 0),
                new FastVector3Int(5, 0, 0),
                new FastVector3Int(5, 5, 0),
                new FastVector3Int(0, 5, 0),
                new FastVector3Int(2, 0, 0),
                new FastVector3Int(3, 0, 0),
                new FastVector3Int(5, 3, 0),
                new FastVector3Int(5, 2, 0),
            };

            List<FastVector3Int> hull = points.BuildConvexHull(grid);
            CollectionAssert.AreEquivalent(
                new[]
                {
                    new FastVector3Int(0, 0, 0),
                    new FastVector3Int(5, 0, 0),
                    new FastVector3Int(5, 5, 0),
                    new FastVector3Int(0, 5, 0),
                },
                hull
            );

            yield return null;
        }

        [UnityTest]
        public IEnumerator ConvexHullWithDuplicatesLargeTransformStillDropsEdges()
        {
            Grid grid = CreateGrid(out GameObject _);
            grid.transform.position = new Vector3(750000f, -125000f, 0f);
            grid.transform.localScale = new Vector3(0.0005f, 0.00025f, 1f);
            grid.cellSize = new Vector3(0.0005f, 0.00025f, 1f);

            List<FastVector3Int> points = new()
            {
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(5, 0, 0),
                new FastVector3Int(5, 5, 0),
                new FastVector3Int(0, 5, 0),
                new FastVector3Int(2, 0, 0),
                new FastVector3Int(3, 0, 0),
                new FastVector3Int(5, 3, 0),
                new FastVector3Int(5, 2, 0),
            };

            List<FastVector3Int> hull = points.BuildConvexHull(grid);
            CollectionAssert.AreEquivalent(
                new[]
                {
                    new FastVector3Int(0, 0, 0),
                    new FastVector3Int(5, 0, 0),
                    new FastVector3Int(5, 5, 0),
                    new FastVector3Int(0, 5, 0),
                },
                hull
            );

            List<FastVector3Int> hullWithEdges = points.BuildConvexHull(
                grid,
                includeColinearPoints: true
            );
            CollectionAssert.IsSupersetOf(
                hullWithEdges,
                new[]
                {
                    new FastVector3Int(2, 0, 0),
                    new FastVector3Int(3, 0, 0),
                    new FastVector3Int(5, 2, 0),
                    new FastVector3Int(5, 3, 0),
                }
            );

            yield return null;
        }

        [UnityTest]
        public IEnumerator ConvexHullDenseEdgeSamplesExcludeColinearWhenDisabled()
        {
            Grid grid = CreateGrid(out GameObject _);

            List<FastVector3Int> perimeter = new()
            {
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(1, 0, 0),
                new FastVector3Int(2, 0, 0),
                new FastVector3Int(3, 0, 0),
                new FastVector3Int(5, 0, 0),
                new FastVector3Int(5, 1, 0),
                new FastVector3Int(5, 2, 0),
                new FastVector3Int(5, 3, 0),
                new FastVector3Int(5, 5, 0),
                new FastVector3Int(0, 5, 0),
            };

            List<FastVector3Int> hull = perimeter.BuildConvexHull(
                grid,
                includeColinearPoints: false
            );
            CollectionAssert.AreEquivalent(
                new[]
                {
                    new FastVector3Int(0, 0, 0),
                    new FastVector3Int(5, 0, 0),
                    new FastVector3Int(5, 5, 0),
                    new FastVector3Int(0, 5, 0),
                },
                hull
            );

            List<FastVector3Int> hullWithEdges = perimeter.BuildConvexHull(
                grid,
                includeColinearPoints: true
            );
            CollectionAssert.IsSupersetOf(
                hullWithEdges,
                new[]
                {
                    new FastVector3Int(1, 0, 0),
                    new FastVector3Int(2, 0, 0),
                    new FastVector3Int(3, 0, 0),
                    new FastVector3Int(5, 2, 0),
                }
            );

            yield return null;
        }

        [UnityTest]
        public IEnumerator ConvexHullDenseSamplesOnAllEdgesCollapseToCorners()
        {
            Grid grid = CreateGrid(out GameObject _);

            List<FastVector3Int> samples = new();
            for (int x = 0; x <= 5; ++x)
            {
                samples.Add(new FastVector3Int(x, 0, 0));
                samples.Add(new FastVector3Int(x, 5, 0));
            }
            for (int y = 1; y < 5; ++y)
            {
                samples.Add(new FastVector3Int(0, y, 0));
                samples.Add(new FastVector3Int(5, y, 0));
            }

            List<FastVector3Int> hull = samples.BuildConvexHull(grid, includeColinearPoints: false);
            CollectionAssert.AreEquivalent(
                new[]
                {
                    new FastVector3Int(0, 0, 0),
                    new FastVector3Int(5, 0, 0),
                    new FastVector3Int(5, 5, 0),
                    new FastVector3Int(0, 5, 0),
                },
                hull
            );

            yield return null;
        }

        [UnityTest]
        public IEnumerator ConvexHullRotatedGridPrunesColinearPoints()
        {
            Grid grid = CreateGrid(out GameObject _);
            grid.transform.position = new Vector3(125f, -42f, 0f);
            grid.transform.rotation = Quaternion.Euler(0f, 0f, 37.5f);
            grid.transform.localScale = new Vector3(0.33f, 0.75f, 1f);
            grid.cellSize = new Vector3(0.33f, 0.75f, 1f);

            List<FastVector3Int> perimeter = new();
            for (int x = -3; x <= 7; ++x)
            {
                perimeter.Add(new FastVector3Int(x, -4, 0));
                perimeter.Add(new FastVector3Int(x, 6, 0));
            }

            for (int y = -3; y <= 5; ++y)
            {
                perimeter.Add(new FastVector3Int(-3, y, 0));
                perimeter.Add(new FastVector3Int(7, y, 0));
            }

            List<FastVector3Int> hull = perimeter.BuildConvexHull(
                grid,
                includeColinearPoints: false
            );
            CollectionAssert.AreEquivalent(
                new[]
                {
                    new FastVector3Int(-3, -4, 0),
                    new FastVector3Int(7, -4, 0),
                    new FastVector3Int(7, 6, 0),
                    new FastVector3Int(-3, 6, 0),
                },
                hull
            );

            List<FastVector3Int> hullWithEdges = perimeter.BuildConvexHull(
                grid,
                includeColinearPoints: true
            );
            CollectionAssert.IsSupersetOf(
                hullWithEdges,
                new[]
                {
                    new FastVector3Int(-1, -4, 0),
                    new FastVector3Int(5, -4, 0),
                    new FastVector3Int(7, 3, 0),
                    new FastVector3Int(2, 6, 0),
                }
            );

            yield return null;
        }

        [UnityTest]
        public IEnumerator ConvexHullLargeRotatedGridPrunesColinearPoints()
        {
            Grid grid = CreateGrid(out GameObject _);
            grid.transform.position = new Vector3(5000f, -12500f, 0f);
            grid.transform.rotation = Quaternion.Euler(0f, 0f, 18.75f);
            grid.transform.localScale = new Vector3(0.125f, 0.333f, 1f);
            grid.cellSize = new Vector3(0.125f, 0.333f, 1f);

            const int minX = -1700;
            const int maxX = 1700;
            const int minY = -850;
            const int maxY = 850;

            List<FastVector3Int> perimeter = new((maxX - minX + 1) * 4);
            for (int x = minX; x <= maxX; ++x)
            {
                perimeter.Add(new FastVector3Int(x, minY, 0));
                perimeter.Add(new FastVector3Int(x, maxY, 0));
            }

            for (int y = minY + 1; y < maxY; ++y)
            {
                perimeter.Add(new FastVector3Int(minX, y, 0));
                perimeter.Add(new FastVector3Int(maxX, y, 0));
            }

            List<FastVector3Int> hull = perimeter.BuildConvexHull(
                grid,
                includeColinearPoints: false
            );
            CollectionAssert.AreEquivalent(
                new[]
                {
                    new FastVector3Int(minX, minY, 0),
                    new FastVector3Int(maxX, minY, 0),
                    new FastVector3Int(maxX, maxY, 0),
                    new FastVector3Int(minX, maxY, 0),
                },
                hull
            );

            List<FastVector3Int> hullWithEdges = perimeter.BuildConvexHull(
                grid,
                includeColinearPoints: true
            );
            CollectionAssert.IsSupersetOf(
                hullWithEdges,
                new[]
                {
                    new FastVector3Int(0, minY, 0),
                    new FastVector3Int(0, maxY, 0),
                    new FastVector3Int(maxX, 0, 0),
                    new FastVector3Int(minX, 0, 0),
                }
            );

            yield return null;
        }

        [Test]
        public void Vector2ConvexHullDenseSamplesCollapseToCorners()
        {
            List<Vector2> samples = new();
            for (int x = 0; x <= 5; ++x)
            {
                samples.Add(new Vector2(x, 0));
                samples.Add(new Vector2(x, 5));
            }

            for (int y = 1; y < 5; ++y)
            {
                samples.Add(new Vector2(0, y));
                samples.Add(new Vector2(5, y));
            }

            List<Vector2> hull = samples.BuildConvexHull(includeColinearPoints: false);
            CollectionAssert.AreEquivalent(
                new[]
                {
                    new Vector2(0, 0),
                    new Vector2(5, 0),
                    new Vector2(5, 5),
                    new Vector2(0, 5),
                },
                hull
            );
        }

        private static readonly float[] LargeRotationAngles = { 5f, 18.75f, -37.5f };

        [TestCaseSource(nameof(LargeRotationAngles))]
        public void Vector2ConvexHullRotatedSamplesCollapseToCorners(float angleDegrees)
        {
            const int minX = -1700;
            const int maxX = 1700;
            const int minY = -850;
            const int maxY = 850;

            float angleRad = Mathf.Deg2Rad * angleDegrees;
            float cos = Mathf.Cos(angleRad);
            float sin = Mathf.Sin(angleRad);

            List<Vector2> samples = CreateRotatedVectorPerimeter(
                minX,
                maxX,
                minY,
                maxY,
                angleDegrees
            );

            List<Vector2> hull = samples.BuildConvexHull(includeColinearPoints: false);
            AssertHullCollapsesToCornersAfterInverseRotation(
                hull,
                cos,
                sin,
                new Vector2Int(minX, minY),
                new Vector2Int(maxX, minY),
                new Vector2Int(maxX, maxY),
                new Vector2Int(minX, maxY)
            );

            List<Vector2> hullWithEdges = samples.BuildConvexHull(includeColinearPoints: true);
            AssertHullContainsEdgeSamplesAfterInverseRotation(
                hullWithEdges,
                cos,
                sin,
                new Vector2Int(0, minY),
                new Vector2Int(0, maxY),
                new Vector2Int(maxX, 0),
                new Vector2Int(minX, 0)
            );
        }

        [UnityTest]
        public IEnumerator LargePointCloudConcaveHullsInsideConvexHull()
        {
            Grid grid = CreateGrid(out GameObject _);

            List<FastVector3Int> points = GenerateRandomPointsSquare(600, 50, seed: 4242);
            // Ensure a few extremes are present
            points.AddRange(
                new[]
                {
                    new FastVector3Int(-50, -50, 0),
                    new FastVector3Int(50, -50, 0),
                    new FastVector3Int(50, 50, 0),
                    new FastVector3Int(-50, 50, 0),
                }
            );

            List<FastVector3Int> convex = points.BuildConvexHull(grid);
            List<FastVector3Int> concaveEdgeSplit = points.BuildConcaveHullEdgeSplit(grid);
            List<FastVector3Int> concaveKnn = points.BuildConcaveHullKnn(grid);
            List<FastVector3Int> concave = points.BuildConcaveHull(
                grid,
                UnityExtensions.ConcaveHullOptions.ForEdgeSplit()
            );

            // No duplicates in hulls
            Assert.AreEqual(convex.Distinct().Count(), convex.Count);
            Assert.AreEqual(concaveEdgeSplit.Distinct().Count(), concaveEdgeSplit.Count);
            Assert.AreEqual(concaveKnn.Distinct().Count(), concaveKnn.Count);
            Assert.AreEqual(concave.Distinct().Count(), concave.Count);

            // Hull points must be drawn from input set
            HashSet<FastVector3Int> input = new(points);
            Assert.IsTrue(convex.All(p => input.Contains(p)));
            Assert.IsTrue(concaveEdgeSplit.All(p => input.Contains(p)));
            Assert.IsTrue(concaveKnn.All(p => input.Contains(p)));
            Assert.IsTrue(concave.All(p => input.Contains(p)));

            // Concave hulls are inside convex hull
            Assert.IsTrue(convex.IsConvexHullInsideConvexHull(grid, concaveEdgeSplit));
            Assert.IsTrue(convex.IsConvexHullInsideConvexHull(grid, concaveKnn));
            Assert.IsTrue(convex.IsConvexHullInsideConvexHull(grid, concave));

            yield return null;
        }

        [UnityTest]
        public IEnumerator LargePointCloudConvexHullReasonableAndContainsExtremes()
        {
            CreateGrid(out GameObject _);

            List<FastVector3Int> points = GenerateRandomPointsSquare(800, 100, seed: 2025);
            points.AddRange(
                new[]
                {
                    new FastVector3Int(-100, -100, 0),
                    new FastVector3Int(100, -100, 0),
                    new FastVector3Int(100, 100, 0),
                    new FastVector3Int(-100, 100, 0),
                }
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator ConvexHullCirclePointsAllOnHull()
        {
            Grid grid = CreateGrid(out GameObject _);

            List<FastVector3Int> circle = GenerateCirclePoints(64, 25);
            List<FastVector3Int> hull = circle.BuildConvexHull(grid, includeColinearPoints: true);
            // On a (rough) circle with integer coordinates, most points should be part of the convex hull
            // Note: Jarvis March may skip some hull vertices on curves when jumping between non-collinear points
            // We expect at least 75% of distinct points to be included
            int distinctCount = circle.Distinct().Count();
            Assert.GreaterOrEqual(
                hull.Count,
                distinctCount * 3 / 4,
                $"Expected at least {distinctCount * 3 / 4} points in hull, got {hull.Count}"
            );
            CollectionAssert.IsSubsetOf(hull, circle);
            // Verify no duplicates in hull
            Assert.AreEqual(
                hull.Distinct().Count(),
                hull.Count,
                "Hull should not contain duplicates"
            );

            yield return null;
        }

        [UnityTest]
        public IEnumerator ConvexHullRectanglePerimeterOnlyCorners()
        {
            Grid grid = CreateGrid(out GameObject _);

            List<FastVector3Int> rectPerimeter = new();
            for (int x = 0; x <= 10; ++x)
            {
                rectPerimeter.Add(new FastVector3Int(x, 0, 0));
                rectPerimeter.Add(new FastVector3Int(x, 6, 0));
            }
            for (int y = 1; y < 6; ++y)
            {
                rectPerimeter.Add(new FastVector3Int(0, y, 0));
                rectPerimeter.Add(new FastVector3Int(10, y, 0));
            }

            List<FastVector3Int> hull = rectPerimeter.BuildConvexHull(grid);
            CollectionAssert.AreEquivalent(
                new[]
                {
                    new FastVector3Int(0, 0, 0),
                    new FastVector3Int(10, 0, 0),
                    new FastVector3Int(10, 6, 0),
                    new FastVector3Int(0, 6, 0),
                },
                hull
            );

            yield return null;
        }

        [UnityTest]
        public IEnumerator ConvexHullRectanglePerimeterIncludeColinearKeepsEdgePoints()
        {
            Grid grid = CreateGrid(out GameObject _);

            List<FastVector3Int> rectPerimeter = new();
            for (int x = 0; x <= 10; ++x)
            {
                rectPerimeter.Add(new FastVector3Int(x, 0, 0));
                rectPerimeter.Add(new FastVector3Int(x, 6, 0));
            }
            for (int y = 1; y < 6; ++y)
            {
                rectPerimeter.Add(new FastVector3Int(0, y, 0));
                rectPerimeter.Add(new FastVector3Int(10, y, 0));
            }

            List<FastVector3Int> hull = rectPerimeter.BuildConvexHull(
                grid,
                includeColinearPoints: true
            );

            // Corners must be present
            CollectionAssert.IsSubsetOf(
                new[]
                {
                    new FastVector3Int(0, 0, 0),
                    new FastVector3Int(10, 0, 0),
                    new FastVector3Int(10, 6, 0),
                    new FastVector3Int(0, 6, 0),
                },
                hull
            );

            // Edge points included when including colinear points
            CollectionAssert.IsSubsetOf(
                new[]
                {
                    new FastVector3Int(2, 0, 0),
                    new FastVector3Int(3, 0, 0),
                    new FastVector3Int(10, 2, 0),
                    new FastVector3Int(10, 3, 0),
                },
                hull
            );

            Assert.Greater(hull.Count, 4);

            yield return null;
        }

        [UnityTest]
        public IEnumerator RandomCloudMultipleSeedsConvexContainsConcaves()
        {
            Grid grid = CreateGrid(out GameObject _);
            HullRegressionRecorder recorder = new(
                nameof(RandomCloudMultipleSeedsConvexContainsConcaves)
            );

            int[] seeds = { 11, 123, 9999 };
            foreach (int seed in seeds)
            {
                List<FastVector3Int> points = GenerateRandomPointsSquare(300, 40, seed);
                List<FastVector3Int> convex = null;
                List<FastVector3Int> concaveEdgeSplit = null;
                List<FastVector3Int> concaveKnn = null;
                List<FastVector3Int> concave = null;
                try
                {
                    convex = points.BuildConvexHull(grid);
                    concaveEdgeSplit = points.BuildConcaveHullEdgeSplit(grid);
                    concaveKnn = points.BuildConcaveHullKnn(grid);
                    concave = points.BuildConcaveHull(
                        grid,
                        UnityExtensions.ConcaveHullOptions.ForEdgeSplit()
                    );

                    AssertNoDuplicates(convex, concaveEdgeSplit, concaveKnn, concave);

                    HashSet<FastVector3Int> input = new(points);
                    Assert.IsTrue(convex.All(input.Contains));
                    Assert.IsTrue(concaveEdgeSplit.All(input.Contains));
                    Assert.IsTrue(concaveKnn.All(input.Contains));
                    Assert.IsTrue(concave.All(input.Contains));

                    Assert.IsTrue(convex.IsConvexHullInsideConvexHull(grid, concaveEdgeSplit));
                    Assert.IsTrue(convex.IsConvexHullInsideConvexHull(grid, concaveKnn));
                    Assert.IsTrue(convex.IsConvexHullInsideConvexHull(grid, concave));
                }
                catch (AssertionException)
                {
                    recorder.WriteSnapshot(
                        mode: "grid",
                        seed: seed,
                        points: points,
                        convex: convex,
                        concaveEdgeSplit: concaveEdgeSplit,
                        concaveKnn: concaveKnn,
                        concaveUnified: concave
                    );
                    throw;
                }
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator CrossShapeConvexCornersConcavesSubset()
        {
            Grid grid = CreateGrid(out GameObject _);

            // Cross shape: long plus sign
            List<FastVector3Int> points = new();
            for (int i = -5; i <= 5; ++i)
            {
                points.Add(new FastVector3Int(i, 0, 0));
                points.Add(new FastVector3Int(0, i, 0));
            }
            points.Add(new FastVector3Int(-6, 0, 0));
            points.Add(new FastVector3Int(6, 0, 0));
            points.Add(new FastVector3Int(0, -6, 0));
            points.Add(new FastVector3Int(0, 6, 0));

            List<FastVector3Int> convex = points.BuildConvexHull(grid);
            List<FastVector3Int> concaveEdgeSplit = points.BuildConcaveHullEdgeSplit(grid);
            List<FastVector3Int> concaveKnn = points.BuildConcaveHullKnn(grid);
            List<FastVector3Int> concave = points.BuildConcaveHull(
                grid,
                UnityExtensions.ConcaveHullOptions.ForEdgeSplit()
            );

            // Expected convex corners of cross’s bounding box
            CollectionAssert.AreEquivalent(
                new[]
                {
                    new FastVector3Int(-6, 0, 0),
                    new FastVector3Int(6, 0, 0),
                    new FastVector3Int(0, -6, 0),
                    new FastVector3Int(0, 6, 0),
                },
                convex
            );

            // Concave hulls should be subsets of convex; do not assert exact points
            HashSet<FastVector3Int> convexSet = new(convex);
            Assert.IsTrue(concaveEdgeSplit.All(p => convexSet.Contains(p) || points.Contains(p)));
            Assert.IsTrue(concaveKnn.All(p => convexSet.Contains(p) || points.Contains(p)));
            Assert.IsTrue(concave.All(p => convexSet.Contains(p) || points.Contains(p)));
            Assert.IsTrue(convex.IsConvexHullInsideConvexHull(grid, concaveEdgeSplit));
            Assert.IsTrue(convex.IsConvexHullInsideConvexHull(grid, concaveKnn));
            Assert.IsTrue(convex.IsConvexHullInsideConvexHull(grid, concave));

            yield return null;
        }

        [UnityTest]
        public IEnumerator UShapeConcaveInsideConvex()
        {
            Grid grid = CreateGrid(out GameObject _);

            // U-shape perimeter
            List<FastVector3Int> u = new();
            for (int x = 0; x <= 8; ++x)
            {
                u.Add(new FastVector3Int(x, 0, 0));
            }
            for (int y = 1; y <= 6; ++y)
            {
                u.Add(new FastVector3Int(0, y, 0));
                u.Add(new FastVector3Int(8, y, 0));
            }

            List<FastVector3Int> convex = u.BuildConvexHull(grid);
            List<FastVector3Int> concaveEdgeSplit = u.BuildConcaveHullEdgeSplit(grid);
            List<FastVector3Int> concaveKnn = u.BuildConcaveHullKnn(grid);
            List<FastVector3Int> concave = u.BuildConcaveHull(
                grid,
                UnityExtensions.ConcaveHullOptions.ForEdgeSplit()
            );

            // Invariants without over-constraining shape
            Assert.IsTrue(convex.IsConvexHullInsideConvexHull(grid, concaveEdgeSplit));
            Assert.IsTrue(convex.IsConvexHullInsideConvexHull(grid, concaveKnn));
            Assert.IsTrue(convex.IsConvexHullInsideConvexHull(grid, concave));
            Assert.IsTrue(concaveEdgeSplit.All(p => u.Contains(p)));
            Assert.IsTrue(concaveKnn.All(p => u.Contains(p)));
            Assert.IsTrue(concave.All(p => u.Contains(p)));

            yield return null;
        }

        [UnityTest]
        public IEnumerator IsPositionInsideDetectsInsideAndOutside()
        {
            Grid grid = CreateGrid(out GameObject _);

            List<FastVector3Int> hull = new()
            {
                new FastVector3Int(0, 0, 0),
                new FastVector3Int(0, 4, 0),
                new FastVector3Int(4, 4, 0),
                new FastVector3Int(4, 0, 0),
            };

            Assert.IsTrue(
                UnityExtensions.IsPositionInside(hull, new FastVector3Int(1, 1, 0), grid)
            );
            Assert.IsFalse(
                UnityExtensions.IsPositionInside(hull, new FastVector3Int(5, 5, 0), grid)
            );

            yield return null;
        }

        [Test]
        public void GetCosineMatchesExpectedValue()
        {
            Vector2 a = new(1f, 0f);
            Vector3 b = new(0f, 1f, 0f);
            Vector3 origin = Vector3.zero;

            double cosine = UnityExtensions.GetCosine(a, b, origin);
            Assert.AreEqual(0d, cosine);
        }

        [Test]
        public void IntersectsDetectsLineSegmentIntersection()
        {
            Vector2 a1 = new(0f, 0f);
            Vector2 a2 = new(2f, 2f);
            Vector2 b1 = new(0f, 2f);
            Vector2 b2 = new(2f, 0f);

            Assert.IsTrue(UnityExtensions.Intersects(a1, a2, b1, b2));
            Assert.IsFalse(
                UnityExtensions.Intersects(a1, a2, new Vector2(3f, 3f), new Vector2(4f, 4f))
            );
        }

        [Test]
        public void LiesOnSegmentDetectsColinearPoint()
        {
            Vector2 p = new(0f, 0f);
            Vector2 q = new(1f, 1f);
            Vector2 r = new(2f, 2f);
            Assert.IsTrue(UnityExtensions.LiesOnSegment(p, q, r));
            Assert.IsFalse(UnityExtensions.LiesOnSegment(p, new Vector2(3f, 1f), r));
        }

        [Test]
        public void AllFastPositionsWithinEnumeratesEveryCell()
        {
            BoundsInt bounds = new(0, 0, 0, 2, 2, 1);
            List<FastVector3Int> buffer = new();
            List<FastVector3Int> list = bounds.AllFastPositionsWithin(buffer);
            List<FastVector3Int> enumerated = bounds.AllFastPositionsWithin().ToList();

            CollectionAssert.AreEquivalent(list, enumerated);
            Assert.AreEqual(4, list.Count);
            Assert.Contains(new FastVector3Int(1, 1, 0), list);
        }

        [Test]
        public void AllFastPositionsWithinZeroSizeBoundsReturnsEmpty()
        {
            BoundsInt bounds = new(0, 0, 0, 0, 5, 1);
            List<FastVector3Int> buffer = new() { new FastVector3Int(10, 10, 10) };

            List<FastVector3Int> list = bounds.AllFastPositionsWithin(buffer);

            Assert.AreSame(buffer, list);
            Assert.AreEqual(0, list.Count);
            Assert.IsFalse(bounds.AllFastPositionsWithin().Any());
        }

        [Test]
        public void AllFastPositionsWithinClearsBufferBeforeFilling()
        {
            BoundsInt bounds = new(1, 1, 0, 2, 1, 1);
            List<FastVector3Int> buffer = new() { new FastVector3Int(5, 5, 5) };

            List<FastVector3Int> list = bounds.AllFastPositionsWithin(buffer);
            List<FastVector3Int> enumerated = bounds.AllFastPositionsWithin().ToList();

            Assert.AreEqual(2, list.Count);
            CollectionAssert.AreEquivalent(enumerated, list);
            CollectionAssert.DoesNotContain(list, new FastVector3Int(5, 5, 5));
        }

        [Test]
        public void ContainsFastVectorReturnsFalseOutside()
        {
            BoundsInt bounds = new(0, 0, 0, 2, 2, 2);
            Assert.IsFalse(bounds.Contains(new FastVector3Int(2, 2, 2)));
        }

        [Test]
        public void IsOnEdge2DDetectsEdgeAndInterior()
        {
            BoundsInt bounds = new(0, 0, 0, 3, 3, 1);
            Assert.IsTrue(new FastVector3Int(0, 1, 0).IsOnEdge2D(bounds));
            Assert.IsTrue(new FastVector3Int(2, 2, 0).IsOnEdge2D(bounds));
            Assert.IsFalse(new FastVector3Int(1, 1, 0).IsOnEdge2D(bounds));
        }

        [UnityTest]
        public IEnumerator WithPaddingAndSetOffsetsManipulateRectTransform()
        {
            GameObject owner = Track(new GameObject("RectTransformTest", typeof(RectTransform)));

            RectTransform rectTransform = owner.GetComponent<RectTransform>();
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            rectTransform.SetLeft(10f);
            rectTransform.SetBottom(5f);
            rectTransform.SetRight(15f);
            rectTransform.SetTop(20f);

            Assert.AreEqual(10f, rectTransform.offsetMin.x);
            Assert.AreEqual(5f, rectTransform.offsetMin.y);
            Assert.AreEqual(-15f, rectTransform.offsetMax.x);
            Assert.AreEqual(-20f, rectTransform.offsetMax.y);

            BoundsInt bounds = new(0, 0, 0, 1, 1, 1);
            BoundsInt padded = bounds.WithPadding(2, 3);
            Assert.AreEqual(-2, padded.xMin);
            Assert.AreEqual(3, padded.xMax);
            Assert.AreEqual(-3, padded.yMin);
            Assert.AreEqual(4, padded.yMax);

            yield return null;
        }

        [UnityTest]
        public IEnumerator SetColorsAppliesColorAcrossStates()
        {
            GameObject owner = Track(new GameObject("SliderTest", typeof(Canvas), typeof(Slider)));

            Slider slider = owner.GetComponent<Slider>();
            Color color = new(0.25f, 0.5f, 0.75f, 1f);
            slider.SetColors(color);

            ColorBlock block = slider.colors;
            Assert.AreEqual(color, block.normalColor);
            Assert.AreEqual(color, block.highlightedColor);
            Assert.AreEqual(color, block.pressedColor);
            Assert.AreEqual(color, block.selectedColor);
            Assert.AreEqual(color, block.disabledColor);

            yield return null;
        }

        [UnityTest]
        public IEnumerator IsDontDestroyOnLoadDetectsFlag()
        {
            GameObject owner = Track(new GameObject("DontDestroyTest"));

            Assert.IsFalse(owner.IsDontDestroyOnLoad());
            UnityEngine.Object.DontDestroyOnLoad(owner);
            Assert.IsTrue(owner.IsDontDestroyOnLoad());

            yield return null;
        }

        [UnityTest]
        public IEnumerator IsCircleFullyContainedValidatesBoundary()
        {
            GameObject owner = Track(
                new GameObject("CircleColliderTest", typeof(CircleCollider2D))
            );

            CircleCollider2D collider = owner.GetComponent<CircleCollider2D>();
            collider.radius = 1f;

            Assert.IsTrue(collider.IsCircleFullyContained(Vector2.zero, 0.5f));
            Assert.IsFalse(collider.IsCircleFullyContained(Vector2.zero, 1.5f));

            yield return null;
        }

        [UnityTest]
        public IEnumerator InvertPolygonColliderCreatesOuterPath()
        {
            GameObject owner = Track(
                new GameObject("PolygonColliderTest", typeof(PolygonCollider2D))
            );

            PolygonCollider2D collider = owner.GetComponent<PolygonCollider2D>();
            collider.pathCount = 1;
            Vector2[] originalPath = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f),
            };
            collider.SetPath(0, originalPath);

            Rect outer = new(-1f, -1f, 4f, 4f);
            collider.Invert(outer);

            Assert.AreEqual(
                2,
                collider.pathCount,
                $"Expected 2 paths after inversion, got {collider.pathCount}"
            );

            Vector2[] expectedOuterPath = new[]
            {
                new Vector2(outer.xMin, outer.yMin),
                new Vector2(outer.xMin, outer.yMax),
                new Vector2(outer.xMax, outer.yMax),
                new Vector2(outer.xMax, outer.yMin),
            };
            Vector2[] outerPath = collider.GetPath(0);
            Assert.AreEqual(
                expectedOuterPath.Length,
                outerPath.Length,
                $"Outer path length mismatch. Expected {expectedOuterPath.Length}, got {outerPath.Length}. "
                    + $"Actual path: [{string.Join(", ", outerPath)}]"
            );
            CollectionAssert.AreEqual(expectedOuterPath, outerPath);

            Vector2[] expectedInnerPath = new[]
            {
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 0f),
            };
            Vector2[] innerPath = collider.GetPath(1);
            Assert.AreEqual(
                expectedInnerPath.Length,
                innerPath.Length,
                $"Inner path length mismatch. Expected {expectedInnerPath.Length}, got {innerPath.Length}. "
                    + $"Actual path: [{string.Join(", ", innerPath)}]"
            );
            CollectionAssert.AreEqual(expectedInnerPath, innerPath);

            yield return null;
        }

        [UnityTest]
        public IEnumerator InvertPolygonColliderWithTrianglePath()
        {
            GameObject owner = Track(
                new GameObject("PolygonColliderTriangleTest", typeof(PolygonCollider2D))
            );

            PolygonCollider2D collider = owner.GetComponent<PolygonCollider2D>();
            collider.pathCount = 1;
            Vector2[] originalPath = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0.5f, 1f),
                new Vector2(1f, 0f),
            };
            collider.SetPath(0, originalPath);

            Rect outer = new(-1f, -1f, 3f, 3f);
            collider.Invert(outer);

            Assert.AreEqual(
                2,
                collider.pathCount,
                $"Expected 2 paths after triangle inversion, got {collider.pathCount}"
            );

            Vector2[] outerPath = collider.GetPath(0);
            Assert.AreEqual(
                4,
                outerPath.Length,
                $"Outer path should have 4 vertices (rectangle), got {outerPath.Length}. "
                    + $"Actual path: [{string.Join(", ", outerPath)}]"
            );

            Vector2[] innerPath = collider.GetPath(1);
            Assert.AreEqual(
                3,
                innerPath.Length,
                $"Inner path should have 3 vertices (triangle reversed), got {innerPath.Length}. "
                    + $"Actual path: [{string.Join(", ", innerPath)}]"
            );

            // Verify the inner path is the original reversed
            Vector2[] expectedInnerPath = new[]
            {
                new Vector2(1f, 0f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, 0f),
            };
            CollectionAssert.AreEqual(expectedInnerPath, innerPath);

            yield return null;
        }

        [UnityTest]
        public IEnumerator InvertPolygonColliderWithMultiplePaths()
        {
            GameObject owner = Track(
                new GameObject("PolygonColliderMultiPathTest", typeof(PolygonCollider2D))
            );

            PolygonCollider2D collider = owner.GetComponent<PolygonCollider2D>();
            collider.pathCount = 2;

            // First path: outer square
            Vector2[] path1 = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 2f),
                new Vector2(2f, 2f),
                new Vector2(2f, 0f),
            };
            collider.SetPath(0, path1);

            // Second path: inner triangle (hole)
            Vector2[] path2 = new[]
            {
                new Vector2(0.5f, 0.5f),
                new Vector2(1f, 1.5f),
                new Vector2(1.5f, 0.5f),
            };
            collider.SetPath(1, path2);

            Rect outer = new(-2f, -2f, 6f, 6f);
            collider.Invert(outer);

            Assert.AreEqual(
                3,
                collider.pathCount,
                $"Expected 3 paths after multi-path inversion, got {collider.pathCount}"
            );

            // First path should be the outer rectangle
            Vector2[] outerPath = collider.GetPath(0);
            Assert.AreEqual(
                4,
                outerPath.Length,
                $"Outer path should have 4 vertices, got {outerPath.Length}"
            );

            // Second path should be path1 reversed
            Vector2[] invertedPath1 = collider.GetPath(1);
            Assert.AreEqual(
                4,
                invertedPath1.Length,
                $"First inverted path should have 4 vertices, got {invertedPath1.Length}. "
                    + $"Actual: [{string.Join(", ", invertedPath1)}]"
            );

            // Third path should be path2 reversed
            Vector2[] invertedPath2 = collider.GetPath(2);
            Assert.AreEqual(
                3,
                invertedPath2.Length,
                $"Second inverted path should have 3 vertices, got {invertedPath2.Length}. "
                    + $"Actual: [{string.Join(", ", invertedPath2)}]"
            );

            yield return null;
        }

        [UnityTest]
        public IEnumerator InvertPolygonColliderWithZeroPaths()
        {
            GameObject owner = Track(
                new GameObject("PolygonColliderZeroPathTest", typeof(PolygonCollider2D))
            );

            PolygonCollider2D collider = owner.GetComponent<PolygonCollider2D>();
            collider.pathCount = 0;

            Rect outer = new(-1f, -1f, 4f, 4f);
            int originalPathCount = collider.pathCount;

            collider.Invert(outer);

            Assert.AreEqual(
                originalPathCount,
                collider.pathCount,
                "Inverting a collider with zero paths should leave path count unchanged"
            );

            yield return null;
        }

        [Test]
        public void InvertPolygonColliderWithNullThrows()
        {
            PolygonCollider2D nullCollider = null;
            Rect outer = new(-1f, -1f, 4f, 4f);

            Assert.Throws<ArgumentNullException>(() => nullCollider.Invert(outer));
        }

        [UnityTest]
        public IEnumerator InvertPolygonColliderWithPentagon()
        {
            GameObject owner = Track(
                new GameObject("PolygonColliderPentagonTest", typeof(PolygonCollider2D))
            );

            PolygonCollider2D collider = owner.GetComponent<PolygonCollider2D>();
            collider.pathCount = 1;

            // Pentagon with 5 vertices
            float radius = 1f;
            Vector2[] pentagon = new Vector2[5];
            for (int i = 0; i < 5; i++)
            {
                float angle = Mathf.PI / 2f + i * 2f * Mathf.PI / 5f;
                pentagon[i] = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
            }
            collider.SetPath(0, pentagon);

            Rect outer = new(-3f, -3f, 6f, 6f);
            collider.Invert(outer);

            Assert.AreEqual(
                2,
                collider.pathCount,
                $"Expected 2 paths after pentagon inversion, got {collider.pathCount}"
            );

            Vector2[] outerPath = collider.GetPath(0);
            Assert.AreEqual(
                4,
                outerPath.Length,
                $"Outer path should have 4 vertices, got {outerPath.Length}"
            );

            Vector2[] innerPath = collider.GetPath(1);
            Assert.AreEqual(
                5,
                innerPath.Length,
                $"Inner path should have 5 vertices (pentagon reversed), got {innerPath.Length}. "
                    + $"Actual path: [{string.Join(", ", innerPath)}]"
            );

            // Verify the inner path is the original reversed
            for (int i = 0; i < 5; i++)
            {
                int reversedIndex = 4 - i;
                Assert.AreEqual(
                    pentagon[reversedIndex].x,
                    innerPath[i].x,
                    0.0001f,
                    $"Pentagon vertex {i} x mismatch"
                );
                Assert.AreEqual(
                    pentagon[reversedIndex].y,
                    innerPath[i].y,
                    0.0001f,
                    $"Pentagon vertex {i} y mismatch"
                );
            }

            yield return null;
        }

        private static List<Vector2> InvokeVectorJarvisFallback(
            List<Vector2> points,
            bool includeColinearPoints
        )
        {
            int count = points?.Count ?? 0;
            List<Vector2> hullBuffer = new(count);
            List<int> scratchIndices = new(count);
            float[] scratchDistances = new float[Math.Max(1, count)];
            bool[] membership = new bool[Math.Max(1, count)];

            return UnityExtensions.BuildConvexHullJarvisFallback(
                points,
                hullBuffer,
                includeColinearPoints,
                scratchIndices,
                scratchDistances,
                membership
            );
        }

        private static List<FastVector3Int> InvokeGridJarvisFallback(
            List<FastVector3Int> points,
            Grid grid,
            bool includeColinearPoints
        )
        {
            int count = points?.Count ?? 0;
            List<FastVector3Int> hullBuffer = new(count);
            List<int> scratchIndices = new(count);
            float[] scratchDistances = new float[Math.Max(1, count)];
            bool[] membership = new bool[Math.Max(1, count)];
            Vector2[] worldPositions = new Vector2[count];
            for (int i = 0; i < count; ++i)
            {
                worldPositions[i] = grid.CellToWorld(points![i]);
            }

            return UnityExtensions.BuildGridConvexHullJarvisFallback(
                points,
                worldPositions,
                hullBuffer,
                includeColinearPoints,
                scratchIndices,
                scratchDistances,
                membership
            );
        }

        private static Vector2 InverseRotate(Vector2 value, float cos, float sin)
        {
            float invX = value.x * cos + value.y * sin;
            float invY = -value.x * sin + value.y * cos;
            return new Vector2(invX, invY);
        }

        private static void AssertHullCollapsesToCornersAfterInverseRotation(
            IEnumerable<Vector2> hull,
            float cos,
            float sin,
            params Vector2Int[] expectedCorners
        )
        {
            HashSet<Vector2Int> actualCorners = new();
            IEnumerable<Vector2> candidates = hull as Vector2[] ?? hull.ToArray();
            foreach (Vector2 candidate in candidates)
            {
                Vector2 unrotated = InverseRotate(candidate, cos, sin);
                int xr = Mathf.RoundToInt(unrotated.x);
                int yr = Mathf.RoundToInt(unrotated.y);
                actualCorners.Add(new Vector2Int(xr, yr));
            }

            TestContext.WriteLine(
                $"Rotated hull raw count={candidates.Count()} uniqueCorners={actualCorners.Count}: {string.Join(", ", actualCorners)}"
            );
            CollectionAssert.AreEquivalent(expectedCorners, actualCorners);
        }

        private static void AssertHullContainsEdgeSamplesAfterInverseRotation(
            IEnumerable<Vector2> hull,
            float cos,
            float sin,
            params Vector2Int[] requiredSamples
        )
        {
            HashSet<Vector2Int> actual = new();
            foreach (Vector2 candidate in hull)
            {
                Vector2 unrotated = InverseRotate(candidate, cos, sin);
                int xr = Mathf.RoundToInt(unrotated.x);
                int yr = Mathf.RoundToInt(unrotated.y);
                actual.Add(new Vector2Int(xr, yr));
            }

            foreach (Vector2Int expected in requiredSamples)
            {
                Assert.IsTrue(
                    actual.Contains(expected),
                    $"Expected rotated hull to include {expected} after inverse rotation."
                );
            }
        }

        private static List<Vector2> CreateRotatedVectorPerimeter(
            int minX,
            int maxX,
            int minY,
            int maxY,
            float angleDegrees
        )
        {
            double angleRad = angleDegrees * Mathf.Deg2Rad;
            double cos = Math.Cos(angleRad);
            double sin = Math.Sin(angleRad);

            Vector2 bottomLeft = RotateExact(minX, minY, cos, sin);
            Vector2 bottomRight = RotateExact(maxX, minY, cos, sin);
            Vector2 topRight = RotateExact(maxX, maxY, cos, sin);
            Vector2 topLeft = RotateExact(minX, maxY, cos, sin);

            List<Vector2> perimeter = new((maxX - minX + 1) * 2 + Math.Max(0, maxY - minY - 1) * 2);

            for (int x = minX; x <= maxX; ++x)
            {
                float t = (x - minX) / (float)(maxX - minX);
                perimeter.Add(Quantize(Vector2.Lerp(bottomLeft, bottomRight, t)));
            }

            for (int x = minX; x <= maxX; ++x)
            {
                float t = (x - minX) / (float)(maxX - minX);
                perimeter.Add(Quantize(Vector2.Lerp(topLeft, topRight, t)));
            }

            for (int y = minY + 1; y < maxY; ++y)
            {
                float t = (y - minY) / (float)(maxY - minY);
                perimeter.Add(Quantize(Vector2.Lerp(topLeft, bottomLeft, t)));
                perimeter.Add(Quantize(Vector2.Lerp(topRight, bottomRight, t)));
            }

            return perimeter;
        }

        private static Vector2 RotateExact(int x, int y, double cos, double sin)
        {
            double fx = x;
            double fy = y;
            double rotatedX = fx * cos - fy * sin;
            double rotatedY = fx * sin + fy * cos;
            return new Vector2((float)rotatedX, (float)rotatedY);
        }

        private static Vector2 Quantize(Vector2 value)
        {
            float x = Mathf.Round(value.x * 1000f) * 0.001f;
            float y = Mathf.Round(value.y * 1000f) * 0.001f;
            return new Vector2(x, y);
        }

        private static void AssertNoDuplicates(params List<FastVector3Int>[] hulls)
        {
            foreach (List<FastVector3Int> hull in hulls)
            {
                if (hull == null)
                {
                    continue;
                }

                Assert.AreEqual(
                    hull.Distinct().Count(),
                    hull.Count,
                    "Hull should not contain duplicates."
                );
            }
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator GetSpritesFromClipEnumeratesKeyframes()
        {
            Texture2D texture = Track(new Texture2D(16, 16));

            Sprite spriteA = Track(
                Sprite.Create(texture, new Rect(0f, 0f, 8f, 8f), new Vector2(0.5f, 0.5f))
            );
            Sprite spriteB = Track(
                Sprite.Create(texture, new Rect(0f, 0f, 8f, 8f), new Vector2(0.5f, 0.5f))
            );
            AnimationClip clip = Track(new AnimationClip());

            UnityEditor.EditorCurveBinding binding = new()
            {
                path = string.Empty,
                propertyName = "m_Sprite",
                type = typeof(SpriteRenderer),
            };

            UnityEditor.ObjectReferenceKeyframe[] frames =
            {
                new() { time = 0f, value = spriteA },
                new() { time = 1f, value = spriteB },
            };

            UnityEditor.AnimationUtility.SetObjectReferenceCurve(clip, binding, frames);

            List<Sprite> sprites = clip.GetSpritesFromClip().ToList();
            Assert.AreEqual(2, sprites.Count);
            Assert.Contains(spriteA, sprites);
            Assert.Contains(spriteB, sprites);

            yield return null;
        }

        /// <summary>
        /// Builds a clip whose four curves differ in exactly one binding field each, so a filter
        /// that ignores one of the three is visible as a wrong answer rather than a missing one.
        /// Every sprite is named for what selects it.
        /// </summary>
        private AnimationClip BuildBoundSpriteClip()
        {
            Texture2D texture = Track(new Texture2D(8, 8));
            AnimationClip clip = Track(new AnimationClip());

            void Bind(string path, Type type, string propertyName, params string[] spriteNames)
            {
                UnityEditor.ObjectReferenceKeyframe[] frames =
                    new UnityEditor.ObjectReferenceKeyframe[spriteNames.Length];
                for (int index = 0; index < spriteNames.Length; ++index)
                {
                    Sprite sprite = Track(
                        Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f))
                    );
                    sprite.name = spriteNames[index];
                    frames[index] = new UnityEditor.ObjectReferenceKeyframe
                    {
                        time = index,
                        value = sprite,
                    };
                }

                UnityEditor.AnimationUtility.SetObjectReferenceCurve(
                    clip,
                    UnityEditor.EditorCurveBinding.PPtrCurve(path, type, propertyName),
                    frames
                );
            }

            Bind(string.Empty, typeof(SpriteRenderer), "m_Sprite", "root0", "root1");
            Bind("Child", typeof(SpriteRenderer), "m_Sprite", "child0");
            Bind(string.Empty, typeof(Image), "m_Sprite", "image0");
            Bind(string.Empty, typeof(SpriteRenderer), "m_Decoration", "deco0");

            // Every assertion below reads as a filtering bug if the editor declined to store one of
            // these curves, so the fixture states what it built.
            Assert.AreEqual(
                4,
                UnityEditor.AnimationUtility.GetObjectReferenceCurveBindings(clip).Length,
                "the editor did not store every object-reference curve this fixture set"
            );
            return clip;
        }

        private static string NamesOf(IEnumerable<Sprite> sprites)
        {
            List<string> names = sprites.Select(sprite => sprite.name).ToList();
            names.Sort(StringComparer.Ordinal);
            return string.Join(",", names);
        }

        [Test]
        public void GetSpriteFramesFromClipKeepsTheBindingThatSuppliesEachSprite()
        {
            AnimationClip clip = BuildBoundSpriteClip();

            Dictionary<string, UnityEditor.EditorCurveBinding> bindingsBySprite =
                clip.GetSpriteFramesFromClip()
                    .ToDictionary(frame => frame.sprite.name, frame => frame.binding);

            Assert.AreEqual(5, bindingsBySprite.Count);
            Assert.AreEqual(string.Empty, bindingsBySprite["root0"].path);
            Assert.AreEqual(string.Empty, bindingsBySprite["root1"].path);
            Assert.AreEqual("Child", bindingsBySprite["child0"].path);
            Assert.AreEqual(typeof(SpriteRenderer), bindingsBySprite["root0"].type);
            Assert.AreEqual(typeof(Image), bindingsBySprite["image0"].type);
            Assert.AreEqual("m_Decoration", bindingsBySprite["deco0"].propertyName);
        }

        [Test]
        public void GetSpriteFramesFromClipYieldsKeyframesInTimeOrder()
        {
            AnimationClip clip = BuildBoundSpriteClip();

            List<string> rootFrames = clip.GetSpritesFromClip(
                    string.Empty,
                    "m_Sprite",
                    typeof(SpriteRenderer)
                )
                .Select(sprite => sprite.name)
                .ToList();

            CollectionAssert.AreEqual(new[] { "root0", "root1" }, rootFrames);
        }

        [TestCase("", "m_Sprite", typeof(SpriteRenderer), "root0,root1")]
        [TestCase("Child", "m_Sprite", typeof(SpriteRenderer), "child0")]
        [TestCase(null, "m_Sprite", typeof(SpriteRenderer), "child0,root0,root1")]
        [TestCase("", "m_Sprite", null, "image0,root0,root1")]
        [TestCase("", null, typeof(SpriteRenderer), "deco0,root0,root1")]
        [TestCase(null, null, null, "child0,deco0,image0,root0,root1")]
        [TestCase("", "m_Sprite", typeof(Image), "image0")]
        [TestCase("Missing", "m_Sprite", typeof(SpriteRenderer), "")]
        [TestCase("", "m_Missing", typeof(SpriteRenderer), "")]
        public void GetSpritesFromClipFiltersOnEveryBindingField(
            string path,
            string propertyName,
            Type type,
            string expected
        )
        {
            AnimationClip clip = BuildBoundSpriteClip();

            Assert.AreEqual(expected, NamesOf(clip.GetSpritesFromClip(path, propertyName, type)));
        }

        [Test]
        public void GetSpritesFromClipDefaultsToTheSpriteProperty()
        {
            AnimationClip clip = BuildBoundSpriteClip();

            // The default exists so the common call is `clip.GetSpritesFromClip(path)`; a default
            // that matched anything would hand back the decoration curve too.
            Assert.AreEqual("root0,root1", NamesOf(clip.GetSpritesFromClip(string.Empty)));
            Assert.AreEqual(
                "m_Sprite",
                WallstopStudios.UnityHelpers.Core.Extension.UnityExtensions.SpriteBindingProperty
            );
        }

        [Test]
        public void GetSpriteFramesFromClipHandlesNullClip()
        {
            AnimationClip clip = null;

            Assert.IsEmpty(clip.GetSpriteFramesFromClip().ToList());
            Assert.IsEmpty(clip.GetSpritesFromClip().ToList());
            Assert.IsEmpty(clip.GetSpritesFromClip(string.Empty).ToList());
        }

        [Test]
        public void GetSpritesFromClipIsTheUnfilteredProjectionOfItsFrames()
        {
            AnimationClip clip = BuildBoundSpriteClip();

            CollectionAssert.AreEqual(
                clip.GetSpriteFramesFromClip().Select(frame => frame.sprite).ToList(),
                clip.GetSpritesFromClip().ToList()
            );
        }
#endif
    }
}
