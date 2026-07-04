// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS || WALLSTOP_CONCAVE_HULL_STATS
#define ENABLE_CONCAVE_HULL_STATS
#endif

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class UnityExtensionsVector2HullTests : CommonTestBase
    {
        [Test]
        public void BuildConvexHullVector2IncludesColinearPointsWhenRequested()
        {
            List<Vector2> points = new()
            {
                new Vector2(0, 0),
                new Vector2(0, 2),
                new Vector2(2, 2),
                new Vector2(2, 0),
                new Vector2(1, 0),
            };

            List<Vector2> hull = points.BuildConvexHull(includeColinearPoints: true);
            CollectionAssert.AreEquivalent(
                points,
                hull,
                "Convex hull should include colinear edge points when requested."
            );
            Assert.AreEqual(
                new Vector2(0, 0),
                hull[0],
                "Hull should start at the lexicographically smallest point."
            );
        }

        [Test]
        public void BuildConvexHullVector2ExcludesColinearPointsWhenDisabled()
        {
            List<Vector2> points = new()
            {
                new Vector2(0, 0),
                new Vector2(0, 2),
                new Vector2(2, 2),
                new Vector2(2, 0),
                new Vector2(1, 0),
            };

            List<Vector2> hull = points.BuildConvexHull(includeColinearPoints: false);
            CollectionAssert.AreEquivalent(
                new[]
                {
                    new Vector2(0, 0),
                    new Vector2(0, 2),
                    new Vector2(2, 2),
                    new Vector2(2, 0),
                },
                hull,
                "Convex hull should exclude colinear interior points when disabled."
            );
        }

        [Test]
        public void BuildConvexHullVector2DeduplicatesRepeatedPoints()
        {
            List<Vector2> points = new()
            {
                new Vector2(0, 0),
                new Vector2(0, 0),
                new Vector2(0, 2),
                new Vector2(2, 2),
                new Vector2(2, 0),
                new Vector2(2, 0),
            };

            List<Vector2> hull = points.BuildConvexHull(includeColinearPoints: true);

            CollectionAssert.AreEquivalent(
                new[]
                {
                    new Vector2(0, 0),
                    new Vector2(0, 2),
                    new Vector2(2, 2),
                    new Vector2(2, 0),
                },
                hull,
                "Duplicate vertices should be removed before hull construction."
            );
        }

        [Test]
        public void BuildConvexHullVector2TreatsNearColinearPointsAsColinear()
        {
            const float epsilon = 1e-6f;
            List<Vector2> points = new()
            {
                new Vector2(0f, 0f),
                new Vector2(2f, 0f),
                new Vector2(2f, 2f),
                new Vector2(0f, 2f),
                new Vector2(1f, epsilon), // almost on the bottom edge
            };
            Vector2 nearlyColinear = new(1f, epsilon);

            List<Vector2> hull = points.BuildConvexHull(includeColinearPoints: false);

            CollectionAssert.DoesNotContain(
                hull,
                nearlyColinear,
                "Near-colinear edge points should be pruned by tolerance."
            );
            CollectionAssert.AreEquivalent(
                new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(2f, 0f),
                    new Vector2(2f, 2f),
                    new Vector2(0f, 2f),
                },
                hull,
                "Hull should consist only of the square's corners."
            );
        }

        [Test]
        public void ConvexHullVector2PermutationInvariance()
        {
            List<Vector2> points = new()
            {
                new Vector2(0, 0),
                new Vector2(3, 0),
                new Vector2(3, 3),
                new Vector2(0, 3),
                new Vector2(1, 1),
            };
            List<Vector2> expected = new()
            {
                new Vector2(0, 0),
                new Vector2(3, 0),
                new Vector2(3, 3),
                new Vector2(0, 3),
            };

            List<Vector2> hullA = points.BuildConvexHull(includeColinearPoints: false);
            points.Reverse();
            List<Vector2> hullB = points.BuildConvexHull(includeColinearPoints: false);
            CollectionAssert.AreEquivalent(
                expected,
                hullA,
                "Hull should be invariant under input permutation (A)."
            );
            CollectionAssert.AreEquivalent(
                expected,
                hullB,
                "Hull should be invariant under input permutation (B)."
            );
        }

        [Test]
        public void IsPointInsideConvexHullVector2DetectsContainment()
        {
            List<Vector2> hull = new()
            {
                new Vector2(0, 0),
                new Vector2(0, 4),
                new Vector2(4, 4),
                new Vector2(4, 0),
            };
            Assert.IsTrue(
                hull.IsConvexHullInsideConvexHull(new List<Vector2> { new(1, 1) }),
                "Point inside square should be reported as inside."
            );
            Assert.IsFalse(
                hull.IsConvexHullInsideConvexHull(new List<Vector2> { new(5, 5) }),
                "Point outside square should be reported as outside."
            );
        }

        [Test]
        public void IsConvexHullInsideConvexHullVector2ValidatesInteriorPoints()
        {
            List<Vector2> outer = new()
            {
                new Vector2(0, 0),
                new Vector2(0, 4),
                new Vector2(4, 4),
                new Vector2(4, 0),
            };
            List<Vector2> inner = new() { new Vector2(1, 1), new Vector2(2, 2), new Vector2(3, 1) };
            Assert.IsTrue(
                outer.IsConvexHullInsideConvexHull(inner),
                "Inner triangle should be inside outer square."
            );
            inner.Add(new Vector2(-1, 0));
            Assert.IsFalse(
                outer.IsConvexHullInsideConvexHull(inner),
                "Adding a point outside should invalidate inside condition."
            );
        }

        private static IEnumerable<TestCaseData> Vector2AxisCornerCases()
        {
            yield return new TestCaseData(
                "Vector2Staircase",
                CreateVector2List((0, 0), (0, 3), (1, 3), (1, 2), (2, 2), (2, 1), (3, 1), (3, 0)),
                4,
                8,
                200f,
                new[] { new Vector2(1, 2), new Vector2(2, 1) }
            ).SetName("ConcaveHullVector2PreservesStaircaseCorners");

            yield return new TestCaseData(
                "Vector2Horseshoe",
                CreateVector2List((0, 0), (0, 5), (1, 5), (1, 1), (4, 1), (4, 5), (5, 5), (5, 0)),
                5,
                8,
                200f,
                new[] { new Vector2(1, 1), new Vector2(4, 1) }
            ).SetName("ConcaveHullVector2PreservesHorseshoeCorners");

            yield return new TestCaseData(
                "Vector2StraightFallback",
                CreateVector2List(
                    (0, 0),
                    (0, 5),
                    (5, 5),
                    (5, 0),
                    (1, 5),
                    (4, 5),
                    (4, 0),
                    (1, 0),
                    (5, 1),
                    (0, 1),
                    (1, 1),
                    (4, 1)
                ),
                5,
                8,
                220f,
                new[] { new Vector2(1, 1), new Vector2(4, 1) }
            ).SetName("ConcaveHullVector2RecoversAxisCornersWithStraightFallback");
        }

        [TestCaseSource(nameof(Vector2AxisCornerCases))]
        public void ConcaveHullVector2PreservesAxisCorners(
            string label,
            List<Vector2> points,
            int nearestNeighbors,
            int bucketSize,
            float angleThreshold,
            Vector2[] requiredCorners
        )
        {
            List<Vector2> edgeSplit = points.BuildConcaveHullEdgeSplit(
                bucketSize: bucketSize,
                angleThreshold: angleThreshold
            );
            List<Vector2> knn = points.BuildConcaveHullKnn(nearestNeighbors);

            AssertRequiredVectorCorners($"{label} edge-split", requiredCorners, edgeSplit);
            AssertRequiredVectorCorners($"{label} knn", requiredCorners, knn);
        }

        [TestCaseSource(nameof(Vector2AxisCornerCases))]
        public void ConcaveHullVector2AxisCornerDiagnostics(
            string label,
            List<Vector2> points,
            int nearestNeighbors,
            int bucketSize,
            float angleThreshold,
            Vector2[] requiredCorners
        )
        {
            List<Vector2> edgeSplit = points.BuildConcaveHullEdgeSplit(
                bucketSize: bucketSize,
                angleThreshold: angleThreshold
            );

            foreach (Vector2 required in requiredCorners)
            {
                if (!edgeSplit.Contains(required))
                {
                    Debug.LogError(
                        $"[GridlessAxisCornerDiagnostics] {label} missing {required}. Hull vertices:\n{string.Join(", ", edgeSplit)}"
                    );
                }
            }

            List<Vector2> knn = points.BuildConcaveHullKnn(nearestNeighbors);
            AssertRequiredVectorCorners($"{label} knn", requiredCorners, knn);
        }

        [Test]
        public void ConcaveHullVector2RepairIsIdempotentAfterAxisCornersInserted()
        {
#if !ENABLE_CONCAVE_HULL_STATS
            Assert.Ignore("ENABLE_CONCAVE_HULL_STATS is not defined for this build.");
#else
            List<Vector2> samples = CreateVector2List(
                (0, 0),
                (0, 3),
                (1, 3),
                (1, 2),
                (2, 2),
                (2, 1),
                (3, 1),
                (3, 0)
            );

            UnityExtensions.ConcaveHullOptions options = UnityExtensions
                .ConcaveHullOptions.Default.WithStrategy(
                    UnityExtensions.ConcaveHullStrategy.EdgeSplit
                )
                .WithBucketSize(8)
                .WithAngleThreshold(220f);

            List<Vector2> repairedHull = samples.BuildConcaveHull(options);
            List<FastVector3Int> fastHull = ConvertVector2CollectionToFast(repairedHull);
            List<FastVector3Int> fastSamples = ConvertVector2CollectionToFast(samples);
            UnityExtensions.ConcaveHullRepairStats stats = UnityExtensions.ProfileConcaveHullRepair(
                fastHull,
                fastSamples,
                options.Strategy,
                options.AngleThreshold
            );

            Assert.AreEqual(
                0,
                stats.AxisCornerInsertions + stats.AxisPathInsertions,
                "Vector2 repair should be a no-op once axis corners already exist."
            );
            Assert.AreEqual(
                0,
                stats.DuplicateRemovals,
                "Vector2 repair must not emit duplicates once the hull is repaired."
            );
#endif
        }

        [Test]
        public void ConcaveHullVector2HandlesPermutedLShapes()
        {
            List<Vector2> basePoints = CreateVector2List(
                (0, 0),
                (0, 4),
                (1, 4),
                (1, 3),
                (2, 3),
                (2, 2),
                (3, 2),
                (3, 1),
                (4, 1),
                (4, 0)
            );
            Vector2[] required = { new Vector2(1, 3), new Vector2(2, 2), new Vector2(3, 1) };
            IRandom random = new PcgRandom(0xBEEF);
            for (int iteration = 0; iteration < 24; ++iteration)
            {
                List<Vector2> permuted = basePoints.OrderBy(_ => random.Next()).ToList();
                List<Vector2> hull = permuted.BuildConcaveHullEdgeSplit(
                    bucketSize: 8,
                    angleThreshold: 220f
                );
                AssertRequiredVectorCorners(
                    $"Vector2 permuted L iteration {iteration}",
                    required,
                    hull
                );
            }
        }

        [Test]
        public void ConcaveHullVector2VariantsMatchConvexHullRectangle()
        {
            List<Vector2> points = new()
            {
                new Vector2(0, 0),
                new Vector2(0, 3),
                new Vector2(3, 3),
                new Vector2(3, 0),
            };
            List<Vector2> convex = points.BuildConvexHull(includeColinearPoints: false);
            List<Vector2> concaveEdgeSplit = points.BuildConcaveHullEdgeSplit();
            List<Vector2> concaveKnn = points.BuildConcaveHullKnn();
            List<Vector2> concave = points.BuildConcaveHull(
                UnityExtensions.ConcaveHullOptions.Default.WithStrategy(
                    UnityExtensions.ConcaveHullStrategy.EdgeSplit
                )
            );

            CollectionAssert.AreEquivalent(
                convex,
                concaveEdgeSplit,
                "Edge-split concave hull should reduce to convex hull for rectangle."
            );
            CollectionAssert.AreEquivalent(
                convex,
                concaveKnn,
                "k-NN concave hull should reduce to convex hull for rectangle."
            );
            CollectionAssert.AreEquivalent(
                convex,
                concave,
                "Unified concave hull should reduce to convex hull for rectangle."
            );
        }

        [Test]
        public void ConcaveHullVector2VariantsMatchConvexHullTriangle()
        {
            List<Vector2> points = new()
            {
                new Vector2(0, 0),
                new Vector2(0, 3),
                new Vector2(3, 3),
                new Vector2(3, 0),
            };
            List<Vector2> convex = points.BuildConvexHull(includeColinearPoints: false);
            List<Vector2> concaveEdgeSplit = points.BuildConcaveHullEdgeSplit();
            List<Vector2> concaveKnn = points.BuildConcaveHullKnn();
            List<Vector2> concave = points.BuildConcaveHull(
                UnityExtensions.ConcaveHullOptions.Default.WithStrategy(
                    UnityExtensions.ConcaveHullStrategy.EdgeSplit
                )
            );

            CollectionAssert.AreEquivalent(
                convex,
                concaveEdgeSplit,
                "Edge-split concave hull should reduce to convex hull for rectangle/triangle set."
            );
            CollectionAssert.AreEquivalent(
                convex,
                concaveKnn,
                "k-NN concave hull should reduce to convex hull for rectangle/triangle set."
            );
            CollectionAssert.AreEquivalent(
                convex,
                concave,
                "Unified concave hull should reduce to convex hull for rectangle/triangle set."
            );
        }

        [Test]
        public void ConcaveHullVector2RandomSetsRespectInvariants()
        {
            IRandom rng = new PcgRandom(7);
            for (int t = 0; t < 10; ++t)
            {
                int count = rng.Next(6, 20);
                List<Vector2> points = new(count);
                for (int i = 0; i < count; ++i)
                {
                    points.Add(new Vector2(rng.Next(-10, 11), rng.Next(-10, 11)));
                }

                List<Vector2> convex = points.BuildConvexHull(includeColinearPoints: false);
                List<Vector2> concaveEdgeSplit = points.BuildConcaveHullEdgeSplit();
                List<Vector2> concaveKnn = points.BuildConcaveHullKnn();
                List<Vector2> concave = points.BuildConcaveHull(
                    UnityExtensions.ConcaveHullOptions.Default.WithStrategy(
                        UnityExtensions.ConcaveHullStrategy.EdgeSplit
                    )
                );

                // No duplicates in results
                Assert.AreEqual(
                    convex.Distinct().Count(),
                    convex.Count,
                    "Convex hull should contain unique vertices."
                );
                Assert.AreEqual(
                    concaveEdgeSplit.Distinct().Count(),
                    concaveEdgeSplit.Count,
                    "Edge-split concave hull should contain unique vertices."
                );
                Assert.AreEqual(
                    concaveKnn.Distinct().Count(),
                    concaveKnn.Count,
                    "k-NN concave hull should contain unique vertices."
                );
                Assert.AreEqual(
                    concave.Distinct().Count(),
                    concave.Count,
                    "Unified concave hull should contain unique vertices."
                );

                // All outputs are subset of inputs
                HashSet<Vector2> inputSet = new(points);
                Assert.IsTrue(
                    convex.All(inputSet.Contains),
                    "All convex hull vertices should be from the input set."
                );
                Assert.IsTrue(
                    concaveEdgeSplit.All(inputSet.Contains),
                    "All edge-split concave hull vertices should be from the input set."
                );
                Assert.IsTrue(
                    concaveKnn.All(inputSet.Contains),
                    "All k-NN concave hull vertices should be from the input set."
                );
                Assert.IsTrue(
                    concave.All(inputSet.Contains),
                    "All unified concave hull vertices should be from the input set."
                );

                // Concave inside convex
                Assert.IsTrue(
                    convex.IsConvexHullInsideConvexHull(concaveEdgeSplit),
                    "Edge-split concave hull must lie inside convex hull."
                );
                Assert.IsTrue(
                    convex.IsConvexHullInsideConvexHull(concaveKnn),
                    "k-NN concave hull must lie inside convex hull."
                );
                Assert.IsTrue(
                    convex.IsConvexHullInsideConvexHull(concave),
                    "Unified concave hull must lie inside convex hull."
                );
            }
        }

        // Keep PR coverage representative; the excluded Performance suite covers 128x128 clouds.
        [Test]
        [Timeout(300000)]
        public void ConcaveHullVector2RepresentativePointCloudAllocationsStayBounded()
        {
            const int width = 32;
            const int height = 32;
            List<Vector2> points = new(width * height);
            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    points.Add(new Vector2(x, y));
                }
            }

            long allocated = GCAssert.MeasureAllocatedBytes(
                () =>
                {
                    List<Vector2> hull = points.BuildConcaveHullKnn(nearestNeighbors: 12);
                    Assert.IsTrue(hull != null);
                    Assert.GreaterOrEqual(hull.Count, 4);
                },
                warmupIterations: 2,
                measuredIterations: 1
            );

            Assert.LessOrEqual(
                allocated,
                16_384,
                $"Vector2 concave hull should allocate no more than the hull list (measured {allocated} bytes)."
            );
        }

        [Test]
        public void ConvexHullVector2HandlesNearColinearNoise()
        {
            List<Vector2> baseLine = new();
            for (int i = 0; i <= 20; ++i)
            {
                baseLine.Add(new Vector2(i, 0f));
            }

            List<Vector2> noisyLine = AddJitter(baseLine, 0.02f);
            noisyLine.Add(new Vector2(5f, 10f));
            noisyLine.Add(new Vector2(10f, -10f));

            List<Vector2> hull = noisyLine.BuildConvexHull(includeColinearPoints: false);

            Assert.IsTrue(
                ContainsApprox(hull, new Vector2(0f, 0f)),
                "Hull should include leftmost point."
            );
            Assert.IsTrue(
                ContainsApprox(hull, new Vector2(10f, -10f)),
                "Hull should include bottom-right point."
            );
            Assert.IsTrue(
                ContainsApprox(hull, new Vector2(5f, 10f)),
                "Hull should include top point."
            );
        }

        [Test]
        public void ConvexHullVector2HandlesNearColinearNoiseRandomSeeds()
        {
            float[] seeds = { 2f, 11f, 42f, 137f };
            foreach (float seed in seeds)
            {
                List<Vector2> line = new();
                for (int i = 0; i <= 30; ++i)
                {
                    line.Add(new Vector2(i, 0f));
                }

                List<Vector2> jittered = AddJitter(line, 0.05f + seed * 0.0001f);
                jittered.Add(new Vector2(0f, -5f));
                jittered.Add(new Vector2(15f, 7f));

                List<Vector2> hull = jittered.BuildConvexHull(includeColinearPoints: false);

                Assert.IsTrue(
                    ContainsApprox(hull, new Vector2(0f, -5f)),
                    $"Hull should contain extreme lower point for seed {seed}."
                );
                Assert.IsTrue(
                    ContainsApprox(hull, new Vector2(15f, 7f)),
                    $"Hull should contain extreme upper point for seed {seed}."
                );
            }
        }

        [Test]
        public void ConcaveHullVector2RejectsSelfIntersection()
        {
            List<Vector2> sShape = new()
            {
                new Vector2(-4f, -1f),
                new Vector2(-2f, 1.5f),
                new Vector2(0f, -1f),
                new Vector2(2f, 1.5f),
                new Vector2(4f, -1f),
                new Vector2(2f, -3f),
                new Vector2(0f, -0.5f),
                new Vector2(-2f, -3f),
            };

            List<Vector2> hull = sShape.BuildConcaveHullKnn(nearestNeighbors: 6);
            Assert.IsFalse(HasSelfIntersection(hull), "Concave hull should not self-intersect.");
        }

        [Test]
        public void ConvexHullVector2MaintainsCounterClockwiseWinding()
        {
            List<Vector2> circle = new();
            const int count = 32;
            for (int i = 0; i < count; ++i)
            {
                float angle = (float)(2 * System.Math.PI * i / count);
                circle.Add(new Vector2(System.MathF.Cos(angle), System.MathF.Sin(angle)));
            }

            List<Vector2> hull = circle.BuildConvexHull(includeColinearPoints: false);
            float area = ComputeSignedArea(hull);
            Assert.Greater(area, 0f, "Convex hull should use counter-clockwise winding.");
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

        private static List<Vector2> AddJitter(IEnumerable<Vector2> points, float maxDeviation)
        {
            IRandom random = new PcgRandom(1337);
            List<Vector2> jittered = new();
            foreach (Vector2 point in points)
            {
                float deviation = (float)(random.NextDouble() - 0.5) * maxDeviation;
                jittered.Add(new Vector2(point.x, point.y + deviation));
            }

            return jittered;
        }

        private static bool ContainsApprox(
            IEnumerable<Vector2> collection,
            Vector2 target,
            float epsilon = 0.05f
        )
        {
            foreach (Vector2 candidate in collection)
            {
                if (Vector2.Distance(candidate, target) <= epsilon)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<Vector2> CreateVector2List(params (int x, int y)[] coords)
        {
            List<Vector2> list = new(coords.Length);
            foreach ((int x, int y) in coords)
            {
                list.Add(new Vector2(x, y));
            }
            return list;
        }

        private static void AssertRequiredVectorCorners(
            string label,
            IEnumerable<Vector2> required,
            IReadOnlyCollection<Vector2> hull
        )
        {
            foreach (Vector2 vertex in required)
            {
                Assert.IsTrue(hull.Contains(vertex), $"{label}: hull should contain {vertex}.");
            }
        }

        private static List<FastVector3Int> ConvertVector2CollectionToFast(
            IEnumerable<Vector2> points
        )
        {
            return points
                .Select(point => new FastVector3Int(
                    (int)Mathf.Round(point.x),
                    (int)Mathf.Round(point.y),
                    0
                ))
                .ToList();
        }
    }
}
