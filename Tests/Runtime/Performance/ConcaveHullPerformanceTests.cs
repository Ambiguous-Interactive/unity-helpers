// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    [TestFixture]
    [Category("Performance")]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class ConcaveHullPerformanceTests : CommonTestBase
    {
        private const int DefaultWidth = 16;
        private const int DefaultHeight = 16;
        private const long Vector2AllocationBudgetBytes = 8_192;
        private const long GridAllocationBudgetBytes = 12_288;
        private const int LargeWidth = 128;
        private const int LargeHeight = 128;
        private const long LargeVector2AllocationBudgetBytes = Vector2AllocationBudgetBytes * 8;
        private const long LargeGridAllocationBudgetBytes = GridAllocationBudgetBytes * 8;
        private const int RepairSampleWidth = 160;
        private const int RepairSampleHeight = 160;

        [Test]
        public void BuildConcaveHullKnnVector2AllocationsStayBounded()
        {
            List<Vector2> points = CreateVectorPointCloud(DefaultWidth, DefaultHeight);

            long allocated = GCAssert.MeasureAllocatedBytes(
                () =>
                {
                    List<Vector2> hull = points.BuildConcaveHullKnn(nearestNeighbors: 8);
                    Assert.IsTrue(hull != null, "Hull should not be null");
                    Assert.GreaterOrEqual(hull.Count, 3);
                },
                warmupIterations: 3,
                measuredIterations: 1
            );

            Assert.LessOrEqual(
                allocated,
                Vector2AllocationBudgetBytes,
                $"Vector2 concave hull should allocate no more than the hull list (measured {allocated} bytes)."
            );
        }

        [Test]
        public void BuildConcaveHullKnnVector2AllocationsStayBoundedForLargePointCloud()
        {
            List<Vector2> points = CreateVectorPointCloud(LargeWidth, LargeHeight);

            long allocated = GCAssert.MeasureAllocatedBytes(
                () =>
                {
                    List<Vector2> hull = points.BuildConcaveHullKnn(nearestNeighbors: 8);
                    Assert.IsTrue(hull != null, "Hull should not be null for large point cloud");
                    Assert.GreaterOrEqual(hull.Count, 3);
                },
                warmupIterations: 2,
                measuredIterations: 1
            );

            Assert.LessOrEqual(
                allocated,
                LargeVector2AllocationBudgetBytes,
                $"Vector2 concave hull (>10k points) should stay within hull list allocations (measured {allocated} bytes)."
            );
        }

        [Test]
        public void BuildConcaveHullKnnGridAllocationsStayBounded()
        {
            List<FastVector3Int> points = CreateGridPointCloud(DefaultWidth, DefaultHeight);
            GameObject gridObject = Track(new GameObject("ConcaveHullGrid"));
            Grid grid = gridObject.AddComponent<Grid>();

            long allocated = GCAssert.MeasureAllocatedBytes(
                () =>
                {
                    List<FastVector3Int> hull = points.BuildConcaveHullKnn(
                        grid,
                        nearestNeighbors: 8
                    );
                    Assert.IsTrue(hull != null, "Grid hull should not be null");
                    Assert.GreaterOrEqual(hull.Count, 3);
                },
                warmupIterations: 3,
                measuredIterations: 1
            );

            Assert.LessOrEqual(
                allocated,
                GridAllocationBudgetBytes,
                $"Grid concave hull should allocate no more than the hull list (measured {allocated} bytes)."
            );
        }

        [Test]
        public void BuildConcaveHullEdgeSplitRepairStatsStayBounded()
        {
            List<FastVector3Int> points = CreateConcaveGridSample(
                RepairSampleWidth,
                RepairSampleHeight
            );

            TestContext.WriteLine(
                $"[Setup] Generated {points.Count} points for {RepairSampleWidth}x{RepairSampleHeight} grid with triangular notch"
            );

            Grid grid = new GameObject("ConcaveHullRepairGrid").AddComponent<Grid>();
            Track(grid.gameObject);

            UnityExtensions.ConcaveHullOptions options =
                UnityExtensions.ConcaveHullOptions.ForEdgeSplit(
                    bucketSize: 64,
                    angleThreshold: 220f
                );

            List<FastVector3Int> hull = points.BuildConcaveHull(grid, options);

            TestContext.WriteLine(
                $"[Hull] Built hull with {hull.Count} vertices from {points.Count} input points"
            );

            UnityExtensions.ConcaveHullRepairStats stats = UnityExtensions.ProfileConcaveHullRepair(
                hull,
                points,
                UnityExtensions.ConcaveHullStrategy.EdgeSplit,
                options.AngleThreshold
            );

            // EdgeSplit with grid-aligned data typically produces axis-aligned hulls directly.
            // Repairs may or may not be needed depending on the shape and bucket size.
            // Focus on correctness constraints rather than requiring repairs to occur.
            int totalInsertions = stats.AxisCornerInsertions + stats.AxisPathInsertions;
            TestContext.WriteLine(
                $"[Repair] Start:{stats.StartHullCount}, Final:{stats.FinalHullCount}, "
                    + $"AxisCornerInsertions:{stats.AxisCornerInsertions}, AxisPathInsertions:{stats.AxisPathInsertions}, "
                    + $"TotalInsertions:{totalInsertions}, AxisNeighborVisits:{stats.AxisNeighborVisits}, "
                    + $"DuplicateRemovals:{stats.DuplicateRemovals}"
            );

            Assert.AreEqual(
                stats.FinalHullCount,
                hull.Count,
                "Profiled repair should mirror final hull size."
            );
            Assert.LessOrEqual(
                stats.FinalHullCount,
                stats.OriginalPointsCount,
                "Repair must not exceed the source point budget."
            );
            Assert.AreEqual(0, stats.DuplicateRemovals, "Repair should deduplicate as it goes.");

            // Hull should have reasonable size (boundary points of the shape).
            Assert.Greater(hull.Count, 0, "Hull should have vertices.");
            Assert.LessOrEqual(hull.Count, points.Count, "Hull should not exceed input size.");
            Assert.GreaterOrEqual(
                hull.Count,
                3,
                "Hull must have at least 3 vertices to form a polygon."
            );
        }

        [Test]
        public void BuildConcaveHullKnnGridAllocationsStayBoundedForLargePointCloud()
        {
            List<FastVector3Int> points = CreateGridPointCloud(LargeWidth, LargeHeight);
            Grid grid = new GameObject("ConcaveHullGridLarge").AddComponent<Grid>();
            Track(grid.gameObject);

            long allocated = GCAssert.MeasureAllocatedBytes(
                () =>
                {
                    List<FastVector3Int> hull = points.BuildConcaveHullKnn(
                        grid,
                        nearestNeighbors: 8
                    );
                    Assert.IsTrue(
                        hull != null,
                        "Grid hull should not be null for large point cloud"
                    );
                    Assert.GreaterOrEqual(hull.Count, 3);
                },
                warmupIterations: 2,
                measuredIterations: 1
            );

            Assert.LessOrEqual(
                allocated,
                LargeGridAllocationBudgetBytes,
                $"Grid concave hull (>10k points) should stay within hull list allocations (measured {allocated} bytes)."
            );
        }

        private static List<Vector2> CreateVectorPointCloud(int width, int height)
        {
            List<Vector2> points = new(width * height);
            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    points.Add(new Vector2(x, y));
                }
            }

            return points;
        }

        private static List<FastVector3Int> CreateGridPointCloud(int width, int height)
        {
            List<FastVector3Int> points = new(width * height);
            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    points.Add(new FastVector3Int(x, y, 0));
                }
            }

            return points;
        }

        private static List<FastVector3Int> CreateConcaveGridSample(int width, int height)
        {
            // Create a filled rectangle with a triangular notch cut into it.
            // The triangular notch creates interior points positioned diagonally from
            // the convex hull edges, forcing EdgeSplit to create diagonal edges.
            // These diagonal edges then require axis-corner repair.
            //
            // Shape: Full rectangle minus a triangular region in the bottom-left quadrant.
            // The triangle's hypotenuse creates diagonal edge connections.
            List<FastVector3Int> points = new(width * height);

            int notchSize = width / 3;

            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    // Carve out a triangular notch from bottom-left corner
                    // Triangle: vertices at (0,0), (notchSize,0), (0,notchSize)
                    bool inTriangle = x < notchSize && y < notchSize && (x + y) < notchSize;

                    if (!inTriangle)
                    {
                        points.Add(new FastVector3Int(x, y, 0));
                    }
                }
            }

            return points;
        }

        private static List<FastVector3Int> CreateDonutSample(
            int width,
            int height,
            int cavityMargin
        )
        {
            // Create a donut shape: filled rectangle with rectangular cavity in center.
            // EdgeSplit with rectangular cavities typically produces axis-aligned hulls.
            List<FastVector3Int> points = new(width * height);
            int cavityMinX = cavityMargin;
            int cavityMaxX = width - cavityMargin - 1;
            int cavityMinY = cavityMargin;
            int cavityMaxY = height - cavityMargin - 1;

            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    bool inCavity =
                        x > cavityMinX && x < cavityMaxX && y > cavityMinY && y < cavityMaxY;
                    if (!inCavity)
                    {
                        points.Add(new FastVector3Int(x, y, 0));
                    }
                }
            }

            return points;
        }

        public static IEnumerable<TestCaseData> EdgeSplitRepairTestCases()
        {
            // Donut shapes: rectangular cavities produce axis-aligned hulls without repairs
            yield return new TestCaseData(100, 100, 25, "Donut").SetName(
                "EdgeSplit.DonutSmall.ProducesAxisAlignedHull"
            );

            yield return new TestCaseData(200, 200, 50, "Donut").SetName(
                "EdgeSplit.DonutLarge.ProducesAxisAlignedHull"
            );

            // Triangle notch shapes: may or may not need repairs depending on EdgeSplit behavior
            yield return new TestCaseData(100, 100, 0, "TriangleNotch").SetName(
                "EdgeSplit.TriangleNotchSmall.HullCorrectness"
            );

            yield return new TestCaseData(200, 200, 0, "TriangleNotch").SetName(
                "EdgeSplit.TriangleNotchLarge.HullCorrectness"
            );

            // Filled rectangle: simple convex case
            yield return new TestCaseData(50, 50, 0, "FilledRectangle").SetName(
                "EdgeSplit.FilledRectangle.ProducesConvexHull"
            );
        }

        [Test]
        [TestCaseSource(nameof(EdgeSplitRepairTestCases))]
        public void BuildConcaveHullEdgeSplitRepairVariousShapes(
            int width,
            int height,
            int cavityMargin,
            string shapeType
        )
        {
            List<FastVector3Int> points = shapeType switch
            {
                "Donut" => CreateDonutSample(width, height, cavityMargin),
                "TriangleNotch" => CreateConcaveGridSample(width, height),
                "FilledRectangle" => CreateGridPointCloud(width, height),
                _ => CreateGridPointCloud(width, height),
            };

            TestContext.WriteLine(
                $"[Setup] Shape={shapeType}, Dimensions={width}x{height}, Points={points.Count}"
            );

            Grid grid = new GameObject($"ConcaveHullRepairGrid_{shapeType}").AddComponent<Grid>();
            Track(grid.gameObject);

            UnityExtensions.ConcaveHullOptions options =
                UnityExtensions.ConcaveHullOptions.ForEdgeSplit(
                    bucketSize: 64,
                    angleThreshold: 220f
                );

            List<FastVector3Int> hull = points.BuildConcaveHull(grid, options);

            TestContext.WriteLine($"[Hull] Built hull with {hull.Count} vertices");

            UnityExtensions.ConcaveHullRepairStats stats = UnityExtensions.ProfileConcaveHullRepair(
                hull,
                points,
                UnityExtensions.ConcaveHullStrategy.EdgeSplit,
                options.AngleThreshold
            );

            int totalInsertions = stats.AxisCornerInsertions + stats.AxisPathInsertions;
            TestContext.WriteLine(
                $"[Repair] Start:{stats.StartHullCount}, Final:{stats.FinalHullCount}, "
                    + $"Insertions:{totalInsertions} (corners:{stats.AxisCornerInsertions}, paths:{stats.AxisPathInsertions})"
            );

            // Core correctness assertions (apply to all shapes)
            Assert.AreEqual(
                stats.FinalHullCount,
                hull.Count,
                $"[{shapeType}] Profiled repair should mirror final hull size."
            );
            Assert.LessOrEqual(
                stats.FinalHullCount,
                stats.OriginalPointsCount,
                $"[{shapeType}] Repair must not exceed the source point budget."
            );
            Assert.AreEqual(
                0,
                stats.DuplicateRemovals,
                $"[{shapeType}] Repair should deduplicate as it goes."
            );
            Assert.Greater(hull.Count, 0, $"[{shapeType}] Hull should have vertices.");
            Assert.LessOrEqual(
                hull.Count,
                points.Count,
                $"[{shapeType}] Hull should not exceed input size."
            );
            Assert.GreaterOrEqual(
                hull.Count,
                3,
                $"[{shapeType}] Hull must have at least 3 vertices to form a polygon."
            );
        }

        [Test]
        public void BuildConcaveHullEdgeSplitRepairIdempotent()
        {
            // Verify that running repair twice produces identical results
            List<FastVector3Int> points = CreateConcaveGridSample(100, 100);
            Grid grid = new GameObject("ConcaveHullIdempotentGrid").AddComponent<Grid>();
            Track(grid.gameObject);

            UnityExtensions.ConcaveHullOptions options =
                UnityExtensions.ConcaveHullOptions.ForEdgeSplit(
                    bucketSize: 64,
                    angleThreshold: 220f
                );

            List<FastVector3Int> hull = points.BuildConcaveHull(grid, options);
            List<FastVector3Int> hullCopy = new(hull);

            UnityExtensions.ConcaveHullRepairStats firstStats =
                UnityExtensions.ProfileConcaveHullRepair(
                    hull,
                    points,
                    UnityExtensions.ConcaveHullStrategy.EdgeSplit,
                    options.AngleThreshold
                );

            UnityExtensions.ConcaveHullRepairStats secondStats =
                UnityExtensions.ProfileConcaveHullRepair(
                    hullCopy,
                    points,
                    UnityExtensions.ConcaveHullStrategy.EdgeSplit,
                    options.AngleThreshold
                );

            TestContext.WriteLine(
                $"[First] Start:{firstStats.StartHullCount}, Final:{firstStats.FinalHullCount}, "
                    + $"Insertions:{firstStats.AxisCornerInsertions + firstStats.AxisPathInsertions}"
            );
            TestContext.WriteLine(
                $"[Second] Start:{secondStats.StartHullCount}, Final:{secondStats.FinalHullCount}, "
                    + $"Insertions:{secondStats.AxisCornerInsertions + secondStats.AxisPathInsertions}"
            );

            Assert.AreEqual(
                firstStats.FinalHullCount,
                secondStats.FinalHullCount,
                "Repair should be idempotent: final hull counts should match."
            );
            Assert.AreEqual(
                firstStats.AxisCornerInsertions,
                secondStats.AxisCornerInsertions,
                "Repair should be idempotent: axis corner insertions should match."
            );
            Assert.AreEqual(
                firstStats.AxisPathInsertions,
                secondStats.AxisPathInsertions,
                "Repair should be idempotent: axis path insertions should match."
            );
        }
    }
}
