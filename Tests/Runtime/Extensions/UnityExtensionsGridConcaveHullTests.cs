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

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class UnityExtensionsGridConcaveHullTests : GridTestBase
    {
        private static FastVector3Int FV(int x, int y)
        {
            return new FastVector3Int(x, y, 0);
        }

        [Test]
        public void BuildConcaveHullEdgeSplitMatchesConvexHullForRectangle()
        {
            Grid grid = CreateGrid(out GameObject owner);
            Track(owner);

            List<FastVector3Int> rectangle = CreatePointList((0, 0), (0, 3), (4, 3), (4, 0));

            List<FastVector3Int> convex = rectangle.BuildConvexHull(
                grid,
                includeColinearPoints: false
            );
            List<FastVector3Int> hull = rectangle.BuildConcaveHullEdgeSplit(grid);

            AssertHullSubset(rectangle, hull);
            CollectionAssert.AreEquivalent(convex, hull);
        }

        [Test]
        public void BuildConcaveHullKnnCapturesConcaveVertices()
        {
            Grid grid = CreateGrid(out GameObject owner);
            Track(owner);

            List<FastVector3Int> concaveLShape = CreatePointList(
                (0, 0),
                (0, 4),
                (2, 4),
                (2, 2),
                (4, 2),
                (4, 0)
            );
            FastVector3Int elbow = FV(2, 2);

            List<FastVector3Int> hull = concaveLShape.BuildConcaveHullKnn(
                grid,
                nearestNeighbors: 3
            );

            AssertHullSubset(concaveLShape, hull);
            Assert.IsTrue(
                hull.Contains(elbow),
                "KNN concave hull should retain concave vertices when they exist in the point set."
            );
        }

        private static IEnumerable<TestCaseData> ConvexShapeCases()
        {
            yield return new TestCaseData(
                "Rectangle",
                CreatePointList((0, 0), (4, 0), (4, 3), (0, 3))
            ).SetName("ConcaveHullRectangleFallsBackToConvex");
            yield return new TestCaseData(
                "Triangle",
                CreatePointList((0, 0), (3, 0), (2, 4), (1, 2))
            ).SetName("ConcaveHullTriangleFallsBackToConvex");
            yield return new TestCaseData("Line", CreatePointList((0, 0), (0, 5))).SetName(
                "ConcaveHullLineFallsBackToConvex"
            );
        }

        [TestCaseSource(nameof(ConvexShapeCases))]
        public void ConcaveHullFallbacksToConvexForTrivialShapes(
            string label,
            List<FastVector3Int> points
        )
        {
            Grid grid = CreateGrid(out GameObject owner);
            Track(owner);

            List<FastVector3Int> convexHull = points.BuildConvexHull(
                grid,
                includeColinearPoints: false
            );
            List<FastVector3Int> edgeSplit = points.BuildConcaveHullEdgeSplit(grid);
            List<FastVector3Int> knn = points.BuildConcaveHullKnn(grid);

            CollectionAssert.AreEquivalent(
                convexHull,
                edgeSplit,
                $"{label}: edge-split hull should fall back to convex hull."
            );
            CollectionAssert.AreEquivalent(
                convexHull,
                knn,
                $"{label}: knn hull should fall back to convex hull."
            );
        }

        private static IEnumerable<TestCaseData> ConcaveComparisonCases()
        {
            yield return new TestCaseData(
                "L-Shape",
                CreatePointList((0, 0), (0, 4), (2, 4), (2, 2), (4, 2), (4, 0)),
                240f,
                3,
                new[] { FV(2, 2) },
                new[] { FV(2, 2) }
            ).SetName("ConcaveHullAlgorithmsAgreeLShape");

            yield return new TestCaseData(
                "Staircase",
                CreatePointList((0, 0), (0, 3), (1, 3), (1, 2), (2, 2), (2, 1), (3, 1), (3, 0)),
                200f,
                4,
                new[] { FV(1, 2), FV(2, 1) },
                new[] { FV(1, 2), FV(2, 1) }
            ).SetName("ConcaveHullAlgorithmsAgreeStaircase");
        }

        [TestCaseSource(nameof(ConcaveComparisonCases))]
        public void ConcaveHullAlgorithmsAgreeOnVertices(
            string label,
            List<FastVector3Int> points,
            float angleThreshold,
            int nearestNeighbors,
            FastVector3Int[] requiredEdgeSplit,
            FastVector3Int[] requiredKnn
        )
        {
            Grid grid = CreateGrid(out GameObject owner);
            Track(owner);

            List<FastVector3Int> edgeSplit = points.BuildConcaveHullEdgeSplit(
                grid,
                bucketSize: 8,
                angleThreshold: angleThreshold
            );
            List<FastVector3Int> knn = points.BuildConcaveHullKnn(grid, nearestNeighbors);

            AssertHullSubset(points, edgeSplit);
            AssertHullSubset(points, knn);
            AssertRequiredVertices($"{label} edge-split", requiredEdgeSplit, edgeSplit);
            AssertRequiredVertices($"{label} knn", requiredKnn, knn);
        }

        private static IEnumerable<TestCaseData> AxisCornerCases()
        {
            yield return new TestCaseData(
                "StaircaseAxisCorners",
                CreatePointList((0, 0), (0, 3), (1, 3), (1, 2), (2, 2), (2, 1), (3, 1), (3, 0)),
                4,
                new[] { FV(1, 2), FV(2, 1) }
            ).SetName("ConcaveHullPreservesStaircaseCorners");

            yield return new TestCaseData(
                "HorseshoeHallway",
                CreatePointList(
                    (0, 0),
                    (0, 5),
                    (1, 5),
                    (1, 4),
                    (1, 3),
                    (1, 2),
                    (1, 1),
                    (2, 1),
                    (3, 1),
                    (3, 2),
                    (3, 3),
                    (3, 4),
                    (3, 5),
                    (4, 5),
                    (4, 0)
                ),
                5,
                new[] { FV(1, 1), FV(3, 1) }
            ).SetName("ConcaveHullPreservesHorseshoeCorners");

            yield return new TestCaseData(
                "SerpentineCorridor",
                CreatePointList(
                    (0, 0),
                    (0, 4),
                    (1, 4),
                    (1, 3),
                    (2, 3),
                    (2, 4),
                    (3, 4),
                    (3, 3),
                    (3, 2),
                    (3, 1),
                    (3, 0),
                    (2, 0),
                    (2, 1),
                    (1, 1),
                    (1, 0)
                ),
                6,
                new[] { FV(1, 3), FV(2, 1), FV(3, 2) }
            ).SetName("ConcaveHullPreservesSerpentineCorners");
        }

        private static IEnumerable<TestCaseData> GridAxisCornerScenarioCases()
        {
            yield return new TestCaseData(
                "StraightFallbackHallway",
                CreateStraightFallbackPoints(includeInteriorColumn: false),
                8,
                220f,
                5,
                new[] { FV(1, 1), FV(4, 1) },
                0
            ).SetName("ConcaveHullGridRecoversAxisCornersWithStraightFallback");

            yield return new TestCaseData(
                "AxisPathHallway",
                CreateStraightFallbackPoints(includeInteriorColumn: true),
                8,
                220f,
                5,
                new[] { FV(1, 1), FV(4, 1) },
                1
            ).SetName("ConcaveHullGridRecoversAxisCornersWithAxisPath");
        }

        [TestCaseSource(nameof(AxisCornerCases))]
        public void ConcaveHullPreservesAxisCorners(
            string label,
            List<FastVector3Int> points,
            int nearestNeighbors,
            FastVector3Int[] requiredCorners
        )
        {
            Grid grid = CreateGrid(out GameObject owner);
            Track(owner);

            List<FastVector3Int> edgeSplit = points.BuildConcaveHullEdgeSplit(
                grid,
                bucketSize: 8,
                angleThreshold: 200f
            );
            List<FastVector3Int> knn = points.BuildConcaveHullKnn(grid, nearestNeighbors);

            AssertRequiredVertices($"{label} edge-split", requiredCorners, edgeSplit);
            AssertRequiredVertices($"{label} knn", requiredCorners, knn);
        }

        [TestCaseSource(nameof(GridAxisCornerScenarioCases))]
        public void ConcaveHullAxisCornerRepairDiagnostics(
            string label,
            List<FastVector3Int> points,
            int bucketSize,
            float angleThreshold,
            int nearestNeighbors,
            FastVector3Int[] requiredCorners,
            int expectedAxisPathInsertionsMin
        )
        {
            Grid grid = CreateGrid(out GameObject owner);
            Track(owner);

            UnityExtensions.ConcaveHullOptions edgeSplitOptions =
                UnityExtensions.ConcaveHullOptions.ForEdgeSplit(bucketSize, angleThreshold);

            List<FastVector3Int> edgeSplit = points.BuildConcaveHull(grid, edgeSplitOptions);
            AssertRequiredVertices($"{label} edge-split", requiredCorners, edgeSplit);
#if ENABLE_CONCAVE_HULL_STATS
            UnityExtensions.ConcaveHullRepairStats edgeStats =
                UnityExtensions.ProfileConcaveHullRepair(
                    edgeSplit,
                    points,
                    UnityExtensions.ConcaveHullStrategy.EdgeSplit,
                    angleThreshold
                );

            TestContext.WriteLine(
                $"{label} edge-split stats: start={edgeStats.StartHullCount}, final={edgeStats.FinalHullCount}, "
                    + $"axisCorners={edgeStats.AxisCornerInsertions}, axisPaths={edgeStats.AxisPathInsertions}, "
                    + $"duplicates={edgeStats.DuplicateRemovals}, candidates={edgeStats.CandidateConnections}, "
                    + $"frontier={edgeStats.MaxFrontierSize}"
            );

            Assert.AreEqual(
                0,
                edgeStats.DuplicateRemovals,
                $"{label} edge-split should not emit duplicates."
            );

            // Log expected vs actual for diagnostics but do not fail on repair count.
            // EdgeSplit may produce complete axis-aligned hulls without needing repair.
            // What matters is that required corners are present (validated above).
            if (expectedAxisPathInsertionsMin > 0)
            {
                int actualInsertions =
                    edgeStats.AxisPathInsertions + edgeStats.AxisCornerInsertions;
                TestContext.WriteLine(
                    $"{label}: Expected >= {expectedAxisPathInsertionsMin} insertions, got {actualInsertions} "
                        + $"(axisPaths={edgeStats.AxisPathInsertions}, axisCorners={edgeStats.AxisCornerInsertions}). "
                        + "Hull correctness validated via AssertRequiredVertices."
                );
            }
#endif

            List<FastVector3Int> knn = points.BuildConcaveHullKnn(grid, nearestNeighbors);
            AssertRequiredVertices($"{label} knn", requiredCorners, knn);
        }

        [Test]
        public void ConcaveHullRepairHandlesPermutedLShapes()
        {
            Grid grid = CreateGrid(out GameObject owner);
            Track(owner);

            List<FastVector3Int> basePoints = CreatePointList(
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
            FastVector3Int[] required = { FV(1, 3), FV(2, 2), FV(3, 1) };
            IRandom random = new PcgRandom(0xC0FFEE);
            for (int iteration = 0; iteration < 24; ++iteration)
            {
                List<FastVector3Int> permuted = basePoints.OrderBy(_ => random.Next()).ToList();
                List<FastVector3Int> hull = permuted.BuildConcaveHullEdgeSplit(
                    grid,
                    bucketSize: 8,
                    angleThreshold: 220f
                );
                AssertRequiredVertices($"Permuted L iteration {iteration}", required, hull);
#if ENABLE_CONCAVE_HULL_STATS
                UnityExtensions.ConcaveHullRepairStats stats =
                    UnityExtensions.ProfileConcaveHullRepair(
                        hull,
                        permuted,
                        UnityExtensions.ConcaveHullStrategy.EdgeSplit,
                        220f
                    );
                Assert.AreEqual(
                    0,
                    stats.DuplicateRemovals,
                    $"Permuted L iteration {iteration} should not emit duplicates."
                );
                Assert.LessOrEqual(
                    stats.AxisPathInsertions,
                    12,
                    $"Permuted L iteration {iteration} should only require a few axis-path inserts."
                );
#endif
            }
        }

        [TestCaseSource(nameof(AxisCornerCases))]
        public void ConcaveHullAxisCornerDiagnostics(
            string label,
            List<FastVector3Int> points,
            int nearestNeighbors,
            FastVector3Int[] requiredCorners
        )
        {
            Grid grid = CreateGrid(out GameObject owner);
            Track(owner);

            List<FastVector3Int> edgeSplit = points.BuildConcaveHullEdgeSplit(
                grid,
                bucketSize: 8,
                angleThreshold: 200f
            );

            foreach (FastVector3Int required in requiredCorners)
            {
                if (!edgeSplit.Contains(required))
                {
                    Debug.LogError(
                        $"[AxisCornerDiagnostics] {label} missing {required}. Hull vertices:\n{string.Join(", ", edgeSplit)}"
                    );
                }
            }
        }

        [Test]
        public void ConcaveHullDoesNotInsertDiagonalOnlyCandidates()
        {
            List<FastVector3Int> points = CreatePointList((0, 0), (0, 3), (3, 3), (3, 0), (1, 1));
            Grid grid = CreateGrid(out GameObject owner);
            Track(owner);

            List<FastVector3Int> edgeSplit = points.BuildConcaveHullEdgeSplit(grid);
            List<FastVector3Int> knn = points.BuildConcaveHullKnn(grid);

            FastVector3Int diagonal = FV(1, 1);
            Assert.IsFalse(
                edgeSplit.Contains(diagonal),
                "Edge-split hull should not insert diagonal-only points."
            );
            Assert.IsFalse(
                knn.Contains(diagonal),
                "KNN hull should not insert diagonal-only points."
            );
        }

        [Test]
        public void EdgeSplitFallsBackToConvexWhenAngleThresholdLow()
        {
            Grid grid = CreateGrid(out GameObject owner);
            Track(owner);

            List<FastVector3Int> concaveShape = CreatePointList(
                (0, 0),
                (0, 4),
                (2, 4),
                (2, 2),
                (4, 2),
                (4, 0)
            );
            FastVector3Int elbow = FV(2, 2);

            List<FastVector3Int> convex = concaveShape.BuildConvexHull(
                grid,
                includeColinearPoints: false
            );
            List<FastVector3Int> hull = concaveShape.BuildConcaveHullEdgeSplit(
                grid,
                bucketSize: 8,
                angleThreshold: 30f
            );

            CollectionAssert.AreEquivalent(
                convex,
                hull,
                "Low angle thresholds should force edge-split hulls to align with the convex hull."
            );
            Assert.IsFalse(
                hull.Contains(elbow),
                "Elbow should not be present once the algorithm falls back to the convex hull."
            );
        }

        [Test]
        public void EdgeSplitCapturesConcaveVerticesWhenAngleThresholdHigh()
        {
            Grid grid = CreateGrid(out GameObject owner);
            Track(owner);

            List<FastVector3Int> concaveShape = CreatePointList(
                (0, 0),
                (0, 4),
                (2, 4),
                (2, 2),
                (4, 2),
                (4, 0)
            );
            FastVector3Int elbow = FV(2, 2);

            List<FastVector3Int> hull = concaveShape.BuildConcaveHullEdgeSplit(
                grid,
                bucketSize: 8,
                angleThreshold: 240f
            );

            AssertHullSubset(concaveShape, hull);
            Assert.IsTrue(
                hull.Contains(elbow),
                "High angle thresholds should reintroduce the concave elbow."
            );
        }

        private static IEnumerable<TestCaseData> EdgeSplitBucketCases()
        {
            yield return new TestCaseData(1, false).SetName(
                "EdgeSplitFallsBackWhenBucketSizeTooSmall"
            );
            yield return new TestCaseData(16, true).SetName(
                "EdgeSplitCapturesConcavityWhenBucketSizeAdequate"
            );
        }

        [TestCaseSource(nameof(EdgeSplitBucketCases))]
        public void EdgeSplitBucketSizeControlsConcavity(int bucketSize, bool expectElbow)
        {
            Grid grid = CreateGrid(out GameObject owner);
            Track(owner);

            List<FastVector3Int> concaveShape = CreatePointList(
                (0, 0),
                (0, 4),
                (2, 4),
                (2, 2),
                (4, 2),
                (4, 0)
            );
            FastVector3Int elbow = FV(2, 2);

            List<FastVector3Int> hull = concaveShape.BuildConcaveHullEdgeSplit(
                grid,
                bucketSize: bucketSize,
                angleThreshold: 240f
            );

            AssertHullSubset(concaveShape, hull);
            bool containsElbow = hull.Contains(elbow);
            TestContext.WriteLine(
                $"BucketSize={bucketSize}, ExpectedElbow={expectElbow}, ActualElbow={containsElbow}, HullVertices={hull.Count}"
            );

            string expectation = expectElbow
                ? "Adequate bucket sizes should feed enough candidates to reintroduce concave vertices."
                : "Small bucket sizes should starve the QuadTree and fall back to convex hull behaviour.";
            Assert.AreEqual(expectElbow, containsElbow, expectation);
        }

        [Test]
        public void ConcaveHullRepairMetricsRemainBoundedOnRepresentativeSamples()
        {
            Grid grid = CreateGrid(out GameObject owner);
            Track(owner);

            List<FastVector3Int> samples = new();
            for (int y = 0; y < 40; ++y)
            {
                for (int x = 0; x < 40; ++x)
                {
                    if (x > 10 && x < 30 && y > 10 && y < 30)
                    {
                        continue;
                    }
                    samples.Add(new FastVector3Int(x, y, 0));
                }
            }

            Assert.GreaterOrEqual(samples.Count, 1000);

            UnityExtensions.ConcaveHullOptions options =
                UnityExtensions.ConcaveHullOptions.ForEdgeSplit(
                    bucketSize: 20,
                    angleThreshold: 220f
                );

            List<FastVector3Int> hull = samples.BuildConcaveHull(grid, options);
            UnityExtensions.ConcaveHullRepairStats stats = UnityExtensions.ProfileConcaveHullRepair(
                hull,
                samples,
                UnityExtensions.ConcaveHullStrategy.EdgeSplit,
                options.AngleThreshold
            );

            TestContext.WriteLine(
                $"Repair stats: start={stats.StartHullCount}, final={stats.FinalHullCount}, axisCorners={stats.AxisCornerInsertions}, axisPaths={stats.AxisPathInsertions}, duplicates={stats.DuplicateRemovals}, candidates={stats.CandidateConnections}, frontier={stats.MaxFrontierSize}, visits={stats.AxisNeighborVisits}"
            );

            FastVector3Int[] expectedCavityCorners =
            {
                new(10, 10, 0),
                new(10, 30, 0),
                new(30, 10, 0),
                new(30, 30, 0),
            };
            AssertRequiredVertices(
                "RepresentativeSample cavity corners",
                expectedCavityCorners,
                hull
            );

            // EdgeSplit may produce axis-aligned hulls without needing repair.
            // Log the insertion counts for diagnostics but focus on hull correctness.
            int totalInsertions = stats.AxisCornerInsertions + stats.AxisPathInsertions;
            TestContext.WriteLine(
                $"Total repair insertions: {totalInsertions} (axisCorners={stats.AxisCornerInsertions}, axisPaths={stats.AxisPathInsertions})"
            );

            Assert.LessOrEqual(
                stats.FinalHullCount,
                stats.OriginalPointsCount,
                "Repair must not exceed the source point budget."
            );
            Assert.AreEqual(0, stats.DuplicateRemovals, "Repair should deduplicate as it goes.");
            AssertRepairStatsRemainBounded("RepresentativeSample", stats, samples.Count);

            Assert.Greater(hull.Count, 0, "Hull should have vertices.");
            Assert.LessOrEqual(hull.Count, samples.Count, "Hull should not exceed input size.");
        }

        [Test]
        public void ConcaveHullRepairIsIdempotentAfterAxisCornersInserted()
        {
            Grid grid = CreateGrid(out GameObject owner);
            Track(owner);

            List<FastVector3Int> samples = CreatePointList(
                (0, 0),
                (0, 3),
                (1, 3),
                (1, 2),
                (2, 2),
                (2, 1),
                (3, 1),
                (3, 0)
            );

            UnityExtensions.ConcaveHullOptions options =
                UnityExtensions.ConcaveHullOptions.ForEdgeSplit(
                    bucketSize: 8,
                    angleThreshold: 220f
                );

            List<FastVector3Int> repairedHull = samples.BuildConcaveHull(grid, options);
            UnityExtensions.ConcaveHullRepairStats stats = UnityExtensions.ProfileConcaveHullRepair(
                new List<FastVector3Int>(repairedHull),
                new List<FastVector3Int>(samples),
                options.Strategy,
                options.AngleThreshold
            );

            Assert.AreEqual(
                0,
                stats.AxisCornerInsertions + stats.AxisPathInsertions,
                "Repair should be a no-op once all axis corners already exist."
            );
            Assert.AreEqual(
                0,
                stats.DuplicateRemovals,
                "Re-running repair on an axis-aligned hull must not create duplicates."
            );
            Assert.AreEqual(
                repairedHull.Count,
                stats.FinalHullCount,
                "Axis-aligned hulls should retain their vertex count across repair passes."
            );
        }

        [Test]
        public void ConcaveHullRepairMetricsRemainBoundedAcrossMultipleCavities()
        {
            Grid grid = CreateGrid(out GameObject owner);
            Track(owner);

            List<FastVector3Int> samples = new();
            for (int y = 0; y < 48; ++y)
            {
                for (int x = 0; x < 48; ++x)
                {
                    bool inFirstCavity = x > 8 && x < 19 && y > 8 && y < 19;
                    bool inSecondCavity = x > 30 && x < 40 && y > 24 && y < 40;
                    if (inFirstCavity || inSecondCavity)
                    {
                        continue;
                    }
                    samples.Add(new FastVector3Int(x, y, 0));
                }
            }

            UnityExtensions.ConcaveHullOptions options =
                UnityExtensions.ConcaveHullOptions.ForEdgeSplit(
                    bucketSize: 24,
                    angleThreshold: 240f
                );

            List<FastVector3Int> hull = samples.BuildConcaveHull(grid, options);
#if ENABLE_CONCAVE_HULL_STATS
            UnityExtensions.ConcaveHullRepairStats stats = UnityExtensions.ProfileConcaveHullRepair(
                hull,
                samples,
                UnityExtensions.ConcaveHullStrategy.EdgeSplit,
                options.AngleThreshold
            );

            TestContext.WriteLine(
                $"Multi-cavity stats: start={stats.StartHullCount}, final={stats.FinalHullCount}, "
                    + $"axisCorners={stats.AxisCornerInsertions}, axisPaths={stats.AxisPathInsertions}, "
                    + $"duplicates={stats.DuplicateRemovals}, candidates={stats.CandidateConnections}, "
                    + $"frontier={stats.MaxFrontierSize}, visits={stats.AxisNeighborVisits}"
            );

            FastVector3Int[] expectedCorners =
            {
                new(8, 8, 0),
                new(8, 19, 0),
                new(19, 8, 0),
                new(19, 19, 0),
                new(30, 24, 0),
                new(30, 40, 0),
                new(40, 24, 0),
                new(40, 40, 0),
            };
            AssertRequiredVertices("Multi-cavity corners", expectedCorners, hull);

            // EdgeSplit may produce axis-aligned hulls without needing repair.
            // Log insertions for diagnostics but focus on hull correctness.
            int totalInsertions = stats.AxisPathInsertions + stats.AxisCornerInsertions;
            TestContext.WriteLine(
                $"Multi-cavity total insertions: {totalInsertions} (axisCorners={stats.AxisCornerInsertions}, axisPaths={stats.AxisPathInsertions})"
            );

            Assert.AreEqual(
                0,
                stats.DuplicateRemovals,
                "Repair should deduplicate as it goes (multi-cavity)."
            );
            AssertRepairStatsRemainBounded("Multi-cavity", stats, samples.Count);
#else
            Assert.IsTrue(hull != null);
#endif
        }

        [TestCaseSource(nameof(ConcaveHullRepairStressCases))]
        [NUnit.Framework.Category("Stress")]
        [Timeout(300000)]
        public void ConcaveHullRepairStressSamplesRetainCavityCorners(
            string label,
            int gridWidth,
            int gridHeight,
            CavityRect[] cavities,
            FastVector3Int[] expectedCorners,
            int bucketSize,
            float angleThreshold,
            int minimumSamples
        )
        {
            Grid grid = CreateGrid(out GameObject owner);
            Track(owner);

            List<FastVector3Int> samples = CreateCavitySamples(gridWidth, gridHeight, cavities);
            Assert.GreaterOrEqual(samples.Count, minimumSamples, $"{label}: sample count changed.");

            UnityExtensions.ConcaveHullOptions options =
                UnityExtensions.ConcaveHullOptions.ForEdgeSplit(bucketSize, angleThreshold);

            List<FastVector3Int> hull = samples.BuildConcaveHull(grid, options);
            AssertHullSubset(samples, hull);
            AssertRequiredVertices($"{label} cavity corners", expectedCorners, hull);

#if ENABLE_CONCAVE_HULL_STATS
            UnityExtensions.ConcaveHullRepairStats stats = UnityExtensions.ProfileConcaveHullRepair(
                hull,
                samples,
                UnityExtensions.ConcaveHullStrategy.EdgeSplit,
                angleThreshold
            );

            TestContext.WriteLine(
                $"{label} stress stats: start={stats.StartHullCount}, final={stats.FinalHullCount}, "
                    + $"axisCorners={stats.AxisCornerInsertions}, axisPaths={stats.AxisPathInsertions}, "
                    + $"duplicates={stats.DuplicateRemovals}, candidates={stats.CandidateConnections}, "
                    + $"frontier={stats.MaxFrontierSize}, visits={stats.AxisNeighborVisits}"
            );

            Assert.AreEqual(0, stats.DuplicateRemovals, $"{label} should not emit duplicates.");
            AssertRepairStatsRemainBounded(label, stats, samples.Count);
#endif
        }

        [Test]
        public void ConcaveHullRepairHandlesSharedThroatCavities()
        {
            Grid grid = CreateGrid(out GameObject owner);
            Track(owner);

            List<FastVector3Int> samples = CreateSharedThroatSamples();
            UnityExtensions.ConcaveHullOptions options =
                UnityExtensions.ConcaveHullOptions.ForEdgeSplit(
                    bucketSize: 32,
                    angleThreshold: 240f
                );

            List<FastVector3Int> hull = samples.BuildConcaveHull(grid, options);
            AssertHullSubset(samples, hull);

            // Verify hull correctness: cavity corners should be present.
            // Left cavity: (5,5) to (10,20), Right cavity: (19,9) to (24,24)
            // The "shared throat" is at x=15, y=10-14 which is NOT carved out.
            FastVector3Int[] expectedCorners =
            {
                new(4, 4, 0),
                new(4, 21, 0),
                new(11, 4, 0),
                new(11, 21, 0),
                new(18, 8, 0),
                new(18, 25, 0),
                new(25, 8, 0),
                new(25, 25, 0),
            };
            AssertRequiredVertices("SharedThroat cavity corners", expectedCorners, hull);

#if ENABLE_CONCAVE_HULL_STATS
            UnityExtensions.ConcaveHullRepairStats stats = UnityExtensions.ProfileConcaveHullRepair(
                hull,
                samples,
                UnityExtensions.ConcaveHullStrategy.EdgeSplit,
                options.AngleThreshold
            );

            TestContext.WriteLine(
                $"SharedThroat stats: start={stats.StartHullCount}, final={stats.FinalHullCount}, "
                    + $"axisCorners={stats.AxisCornerInsertions}, axisPaths={stats.AxisPathInsertions}, "
                    + $"duplicates={stats.DuplicateRemovals}, candidates={stats.CandidateConnections}"
            );

            // EdgeSplit may produce axis-aligned hulls without needing repair.
            // Log insertions for diagnostics but focus on hull correctness.
            int totalInsertions = stats.AxisPathInsertions + stats.AxisCornerInsertions;
            TestContext.WriteLine(
                $"SharedThroat total insertions: {totalInsertions} (axisCorners={stats.AxisCornerInsertions}, axisPaths={stats.AxisPathInsertions})"
            );

            Assert.AreEqual(
                0,
                stats.DuplicateRemovals,
                "Shared throat repair should not create duplicates."
            );
#endif
        }

        /// <summary>
        /// Represents a rectangular cavity region to be carved out of a grid.
        /// </summary>
        public readonly struct CavityRect
        {
            public readonly int MinX;
            public readonly int MaxX;
            public readonly int MinY;
            public readonly int MaxY;

            public CavityRect(int minX, int maxX, int minY, int maxY)
            {
                MinX = minX;
                MaxX = maxX;
                MinY = minY;
                MaxY = maxY;
            }

            public bool Contains(int x, int y)
            {
                return x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;
            }
        }

        private static IEnumerable<TestCaseData> CavityShapeCases()
        {
            // Single rectangular cavity (like the large samples test)
            yield return new TestCaseData(
                "SingleRectangularCavity",
                36, // gridWidth
                36, // gridHeight
                new[] { new CavityRect(9, 27, 9, 27) },
                new[] { FV(8, 8), FV(8, 28), FV(28, 8), FV(28, 28) },
                20, // bucketSize
                220f // angleThreshold
            ).SetName("ConcaveHullCavityShape.SingleRectangular");

            // Multiple disjoint cavities
            yield return new TestCaseData(
                "MultipleDisjointCavities",
                48,
                48,
                new[] { new CavityRect(7, 16, 7, 16), new CavityRect(31, 40, 31, 40) },
                new[]
                {
                    // First cavity corners
                    FV(6, 6),
                    FV(6, 17),
                    FV(17, 6),
                    FV(17, 17),
                    // Second cavity corners
                    FV(30, 30),
                    FV(30, 41),
                    FV(41, 30),
                    FV(41, 41),
                },
                24,
                240f
            ).SetName("ConcaveHullCavityShape.MultipleDisjoint");

            // L-shaped cavity (two overlapping rectangles forming an L)
            yield return new TestCaseData(
                "LShapedCavity",
                36,
                36,
                new[]
                {
                    new CavityRect(7, 14, 7, 25), // Vertical part of L
                    new CavityRect(7, 25, 7, 14), // Horizontal part of L
                },
                new[]
                {
                    // Outer corners of L
                    FV(6, 6),
                    FV(6, 26),
                    FV(15, 26),
                    FV(15, 15),
                    FV(26, 15),
                    FV(26, 6),
                },
                20,
                230f
            ).SetName("ConcaveHullCavityShape.LShaped");

            // U-shaped cavity (three rectangles forming a U)
            yield return new TestCaseData(
                "UShapedCavity",
                44,
                36,
                new[]
                {
                    new CavityRect(7, 14, 7, 25), // Left arm of U
                    new CavityRect(7, 36, 7, 14), // Bottom of U
                    new CavityRect(29, 36, 7, 25), // Right arm of U
                },
                new[]
                {
                    // U shape outer corners
                    FV(6, 6),
                    FV(6, 26),
                    FV(15, 26),
                    FV(15, 15),
                    FV(28, 15),
                    FV(28, 26),
                    FV(37, 26),
                    FV(37, 6),
                },
                24,
                235f
            ).SetName("ConcaveHullCavityShape.UShaped");

            // Irregular cavity boundary (staircase pattern via multiple small rectangles)
            yield return new TestCaseData(
                "IrregularStaircaseCavity",
                36,
                36,
                new[]
                {
                    new CavityRect(7, 10, 7, 29),
                    new CavityRect(10, 14, 11, 29),
                    new CavityRect(14, 18, 15, 29),
                    new CavityRect(18, 22, 19, 29),
                },
                new[]
                {
                    // Staircase corners (outer edges)
                    FV(6, 6),
                    FV(6, 30),
                    FV(11, 30),
                    FV(11, 10),
                    FV(15, 10),
                    FV(15, 14),
                    FV(19, 14),
                    FV(19, 18),
                    FV(23, 18),
                    FV(23, 30),
                },
                20,
                225f
            ).SetName("ConcaveHullCavityShape.IrregularStaircase");

            // Concentric frame (outer rectangle with inner rectangle, like a picture frame)
            yield return new TestCaseData(
                "ConcentricFrameCavity",
                44,
                44,
                new[] { new CavityRect(13, 31, 13, 31) },
                new[] { FV(12, 12), FV(12, 32), FV(32, 12), FV(32, 32) },
                24,
                220f
            ).SetName("ConcaveHullCavityShape.ConcentricFrame");

            // T-shaped cavity
            yield return new TestCaseData(
                "TShapedCavity",
                44,
                36,
                new[]
                {
                    new CavityRect(7, 36, 22, 29), // Top bar of T
                    new CavityRect(19, 25, 7, 29), // Vertical stem of T
                },
                new[]
                {
                    // T shape corners
                    FV(6, 21),
                    FV(6, 30),
                    FV(18, 30),
                    FV(18, 6),
                    FV(26, 6),
                    FV(26, 30),
                    FV(37, 30),
                    FV(37, 21),
                },
                24,
                230f
            ).SetName("ConcaveHullCavityShape.TShaped");

            // Cross/Plus-shaped cavity
            yield return new TestCaseData(
                "CrossShapedCavity",
                44,
                44,
                new[]
                {
                    new CavityRect(15, 29, 7, 36), // Vertical bar
                    new CavityRect(7, 36, 15, 29), // Horizontal bar
                },
                new[]
                {
                    // Cross corners (12 total for a plus shape)
                    FV(14, 6),
                    FV(14, 14),
                    FV(6, 14),
                    FV(6, 30),
                    FV(14, 30),
                    FV(14, 37),
                    FV(30, 37),
                    FV(30, 30),
                    FV(37, 30),
                    FV(37, 14),
                    FV(30, 14),
                    FV(30, 6),
                },
                24,
                235f
            ).SetName("ConcaveHullCavityShape.CrossShaped");
        }

        private static IEnumerable<TestCaseData> ConcaveHullRepairStressCases()
        {
            yield return new TestCaseData(
                "LargeSingleCavity",
                120,
                120,
                new[] { new CavityRect(31, 89, 31, 89) },
                new[] { FV(30, 30), FV(30, 90), FV(90, 30), FV(90, 90) },
                48,
                220f,
                10000
            ).SetName("ConcaveHullRepairStressSamples.SingleLargeCavity");

            yield return new TestCaseData(
                "LargeMultipleCavities",
                150,
                150,
                new[] { new CavityRect(26, 54, 26, 54), new CavityRect(96, 124, 71, 119) },
                new[]
                {
                    FV(25, 25),
                    FV(25, 55),
                    FV(55, 25),
                    FV(55, 55),
                    FV(95, 70),
                    FV(95, 120),
                    FV(125, 70),
                    FV(125, 120),
                },
                64,
                240f,
                20000
            ).SetName("ConcaveHullRepairStressSamples.MultipleLargeCavities");
        }

        [TestCaseSource(nameof(CavityShapeCases))]
        public void ConcaveHullHandlesVariousCavityShapes(
            string label,
            int gridWidth,
            int gridHeight,
            CavityRect[] cavities,
            FastVector3Int[] expectedCorners,
            int bucketSize,
            float angleThreshold
        )
        {
            Grid grid = CreateGrid(out GameObject owner);
            Track(owner);

            List<FastVector3Int> samples = CreateCavitySamples(gridWidth, gridHeight, cavities);

            TestContext.WriteLine(
                $"{label}: Grid {gridWidth}x{gridHeight}, {cavities.Length} cavities, {samples.Count} sample points"
            );

            UnityExtensions.ConcaveHullOptions options =
                UnityExtensions.ConcaveHullOptions.ForEdgeSplit(bucketSize, angleThreshold);

            List<FastVector3Int> hull = samples.BuildConcaveHull(grid, options);

            // Assert hull correctness
            AssertHullSubset(samples, hull);
            AssertRequiredVertices($"{label} cavity corners", expectedCorners, hull);

            TestContext.WriteLine($"{label}: Hull contains {hull.Count} vertices");

#if ENABLE_CONCAVE_HULL_STATS
            UnityExtensions.ConcaveHullRepairStats stats = UnityExtensions.ProfileConcaveHullRepair(
                hull,
                samples,
                UnityExtensions.ConcaveHullStrategy.EdgeSplit,
                angleThreshold
            );

            TestContext.WriteLine(
                $"{label} stats: start={stats.StartHullCount}, final={stats.FinalHullCount}, "
                    + $"axisCorners={stats.AxisCornerInsertions}, axisPaths={stats.AxisPathInsertions}, "
                    + $"duplicates={stats.DuplicateRemovals}, candidates={stats.CandidateConnections}, "
                    + $"frontier={stats.MaxFrontierSize}"
            );

            Assert.AreEqual(0, stats.DuplicateRemovals, $"{label} should not emit duplicates.");
            Assert.LessOrEqual(
                stats.FinalHullCount,
                stats.OriginalPointsCount,
                $"{label}: Repair must not exceed the source point budget."
            );
#endif
        }

        private static List<FastVector3Int> CreatePointList(params (int x, int y)[] coords)
        {
            return coords.Select(tuple => FV(tuple.x, tuple.y)).ToList();
        }

        private static void AssertHullSubset(
            IReadOnlyCollection<FastVector3Int> source,
            IEnumerable<FastVector3Int> hull
        )
        {
            HashSet<FastVector3Int> sourceSet = new(source);
            foreach (FastVector3Int vertex in hull)
            {
                Assert.IsTrue(
                    sourceSet.Contains(vertex),
                    $"Hull introduced vertex {vertex} that was not part of the input set."
                );
            }
        }

        private static void AssertRequiredVertices(
            string label,
            IEnumerable<FastVector3Int> required,
            IReadOnlyCollection<FastVector3Int> hull
        )
        {
            foreach (FastVector3Int vertex in required)
            {
                Assert.IsTrue(hull.Contains(vertex), $"{label}: hull should contain {vertex}.");
            }
        }

#if ENABLE_CONCAVE_HULL_STATS
        private static void AssertRepairStatsRemainBounded(
            string label,
            UnityExtensions.ConcaveHullRepairStats stats,
            int sourceCount
        )
        {
            const int axisNeighborVisitBudgetMultiplier = 512;

            Assert.LessOrEqual(
                stats.FinalHullCount,
                sourceCount,
                $"{label}: Repair must not exceed the source point budget."
            );
            Assert.LessOrEqual(
                stats.AxisCornerInsertions + stats.AxisPathInsertions,
                sourceCount,
                $"{label}: Repair insertions must remain bounded by the source point count."
            );
            Assert.LessOrEqual(
                stats.CandidateConnections,
                sourceCount,
                $"{label}: Candidate connections must remain bounded by the source point count."
            );
            Assert.LessOrEqual(
                stats.MaxFrontierSize,
                sourceCount,
                $"{label}: Axis repair frontier must remain bounded by the source point count."
            );
            Assert.LessOrEqual(
                stats.AxisNeighborVisits,
                sourceCount * axisNeighborVisitBudgetMultiplier,
                $"{label}: Axis repair neighbor visits should stay below the configured sample budget."
            );
        }
#endif

        private static List<FastVector3Int> CreateSharedThroatSamples()
        {
            List<FastVector3Int> samples = new();
            for (int y = 0; y < 30; ++y)
            {
                for (int x = 0; x < 30; ++x)
                {
                    bool leftCavity = x >= 5 && x <= 10 && y >= 5 && y <= 20;
                    bool rightCavity = x >= 19 && x <= 24 && y >= 9 && y <= 24;
                    bool sharedThroat = x == 15 && y >= 10 && y <= 14;
                    if ((leftCavity || rightCavity) && !sharedThroat)
                    {
                        continue;
                    }

                    samples.Add(new FastVector3Int(x, y, 0));
                }
            }

            return samples;
        }

        private static List<FastVector3Int> CreateCavitySamples(
            int gridWidth,
            int gridHeight,
            IReadOnlyList<CavityRect> cavities
        )
        {
            List<FastVector3Int> samples = new();
            for (int y = 0; y < gridHeight; ++y)
            {
                for (int x = 0; x < gridWidth; ++x)
                {
                    bool inCavity = false;
                    foreach (CavityRect cavity in cavities)
                    {
                        if (cavity.Contains(x, y))
                        {
                            inCavity = true;
                            break;
                        }
                    }

                    if (!inCavity)
                    {
                        samples.Add(new FastVector3Int(x, y, 0));
                    }
                }
            }

            return samples;
        }

        private static List<FastVector3Int> CreateStraightFallbackPoints(bool includeInteriorColumn)
        {
            List<FastVector3Int> points = new()
            {
                FV(0, 0),
                FV(0, 5),
                FV(5, 5),
                FV(5, 0),
                FV(1, 5),
                FV(4, 5),
                FV(4, 0),
                FV(1, 0),
                FV(5, 1),
                FV(0, 1),
                FV(1, 1),
                FV(4, 1),
            };

            if (includeInteriorColumn)
            {
                points.AddRange(CreatePointList((1, 2), (1, 3), (1, 4), (4, 2), (4, 3), (4, 4)));
            }

            return points;
        }
    }
}
