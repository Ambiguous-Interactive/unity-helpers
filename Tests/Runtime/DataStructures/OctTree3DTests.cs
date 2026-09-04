// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;
    using Vector3 = UnityEngine.Vector3;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class OctTree3DTests : SpatialTree3DTests<OctTree3D<Vector3>>
    {
        /*
            A fixed seed, not PRNG.Instance: that hands out an instance seeded from Guid.NewGuid(),
            so a failing case cannot be replayed. SetUp reseeds it, which is what makes running one
            test alone produce the data it produced inside the whole fixture.
        */
        private const uint RandomSeed = 0x5EED0303;

        private IRandom _random = new PcgRandom(RandomSeed);

        private IRandom Random => _random;

        [SetUp]
        public void SeedOctTree3DRandom()
        {
            _random = new PcgRandom(RandomSeed);
        }

        protected override OctTree3D<Vector3> CreateTree(IEnumerable<Vector3> points)
        {
            return new OctTree3D<Vector3>(points, point => point);
        }

        [Test]
        public void ConstructorWithNullPointsThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                new OctTree3D<Vector3>(null, point => point);
            });
        }

        [Test]
        public void ConstructorWithNullTransformerThrowsArgumentNullException()
        {
            List<Vector3> points = new() { Vector3.zero };
            Assert.Throws<ArgumentNullException>(() =>
            {
                new OctTree3D<Vector3>(points, null);
            });
        }

        [Test]
        public void ConstructorWithEmptyCollectionSucceeds()
        {
            List<Vector3> points = new();
            OctTree3D<Vector3> tree = CreateTree(points);
            Assert.IsTrue(tree != null);

            List<Vector3> results = new();
            tree.GetElementsInRange(Vector3.zero, 100f, results);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void ConstructorWithSingleElementSucceeds()
        {
            Vector3 point = new(
                Random.NextFloat(-100, 100),
                Random.NextFloat(-100, 100),
                Random.NextFloat(-100, 100)
            );
            List<Vector3> points = new() { point };
            OctTree3D<Vector3> tree = CreateTree(points);

            List<Vector3> results = new();
            tree.GetElementsInRange(point, 1f, results);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(point, results[0]);
        }

        [Test]
        public void ConstructorWithDuplicateElementsPreservesAll()
        {
            Vector3 point = new(4f, -2f, 1f);
            List<Vector3> points = new() { point, point, point };
            OctTree3D<Vector3> tree = CreateTree(points);

            List<Vector3> results = new();
            tree.GetElementsInRange(point, 0.01f, results);
            Assert.AreEqual(3, results.Count);
            Assert.IsTrue(results.All(candidate => candidate == point));
        }

        [Test]
        public void GetElementsInRangeWithEmptyTreeReturnsEmptyAdditional()
        {
            OctTree3D<Vector3> tree = CreateTree(new List<Vector3>());
            List<Vector3> results = new();

            tree.GetElementsInRange(Vector3.zero, 10f, results);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetElementsInRangeWithZeroRangeReturnsOnlyExactMatchesAdditional()
        {
            Vector3 target = new(8f, 3f, -5f);
            List<Vector3> points = new()
            {
                target,
                target,
                target,
                target + new Vector3(0.02f, 0f, 0f),
                target + new Vector3(0f, 0.02f, 0f),
            };

            OctTree3D<Vector3> tree = CreateTree(points);
            List<Vector3> results = new() { new Vector3(-999f, -999f, -999f) };

            tree.GetElementsInRange(target, 0f, results);
            Assert.AreEqual(3, results.Count);
            Assert.IsTrue(results.All(candidate => candidate == target));
        }

        [Test]
        public void GetElementsInRangeWithVeryLargeRangeReturnsAllPointsAdditional()
        {
            List<Vector3> points = new();
            for (int i = 0; i < 96; ++i)
            {
                points.Add(
                    new Vector3(
                        Random.NextFloat(-40, 40),
                        Random.NextFloat(-40, 40),
                        Random.NextFloat(-40, 40)
                    )
                );
            }

            OctTree3D<Vector3> tree = CreateTree(points);
            List<Vector3> results = new();

            tree.GetElementsInRange(Vector3.zero, 100f, results);
            CollectionAssert.AreEquivalent(points, results);
        }

        [Test]
        public void GetElementsInRangeWithMinimumRangeExcludesNearElements()
        {
            List<Vector3> points = new()
            {
                new Vector3(0f, 0f, 1f),
                new Vector3(0f, 0f, 3f),
                new Vector3(0f, 0f, 5f),
                new Vector3(0f, 0f, 7f),
            };

            OctTree3D<Vector3> tree = CreateTree(points);
            List<Vector3> results = new();
            tree.GetElementsInRange(Vector3.zero, 5.5f, results, minimumRange: 2f);

            Vector3[] expected = { points[1], points[2] };
            CollectionAssert.AreEquivalent(expected, results);
        }

        [Test]
        public void GetElementsInRangeFullyContainedNodeOutsideMinimumReturnsAll()
        {
            List<Vector3> cluster = new();
            Vector3 center = new(25f, -10f, 5f);
            for (int i = 0; i < 24; ++i)
            {
                float offset = i * 0.1f;
                cluster.Add(center + new Vector3(offset, -offset * 0.5f, offset * 0.25f));
            }

            OctTree3D<Vector3> tree = CreateTree(cluster);
            List<Vector3> results = new();

            tree.GetElementsInRange(Vector3.zero, 200f, results, minimumRange: 5f);

            CollectionAssert.AreEquivalent(cluster, results);
        }

        [Test]
        public void GetElementsInRangeFullyContainedNodeIntersectingMinimumFiltersPoints()
        {
            List<Vector3> points = new()
            {
                new Vector3(0.5f, 0f, 0f),
                new Vector3(1.5f, 0.5f, 0f),
                new Vector3(3f, 0f, 0f),
                new Vector3(6f, 0f, 0f),
            };

            OctTree3D<Vector3> tree = CreateTree(points);
            List<Vector3> results = new();

            tree.GetElementsInRange(Vector3.zero, 10f, results, minimumRange: 2f);

            Vector3[] expected = { points[2], points[3] };
            CollectionAssert.AreEquivalent(expected, results);
        }

        [Test]
        public void GetElementsInBoundsReturnsIntersectingPoints()
        {
            List<Vector3> points = new()
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(3f, 3f, 3f),
                new Vector3(6f, 6f, 6f),
                new Vector3(12f, 12f, 12f),
            };

            OctTree3D<Vector3> tree = CreateTree(points);
            Bounds bounds = new(new Vector3(4f, 4f, 4f), new Vector3(8f, 8f, 8f));
            List<Vector3> results = new();

            tree.GetElementsInBounds(bounds, results);
            BoundingBox3D queryBounds = BoundingBox3D.FromClosedBounds(bounds);
            List<Vector3> expected = points.Where(queryBounds.Contains).ToList();
            CollectionAssert.AreEquivalent(expected, results);
        }

        [Test]
        public void GetElementsInBoundsTreatsUpperBoundaryAsExclusive()
        {
            List<Vector3> points = new() { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f) };

            OctTree3D<Vector3> tree = CreateTree(points);
            KdTree3D<Vector3> kd = new(points, p => p);
            Bounds bounds = new(
                center: new Vector3(0.5f, 0f, 0f),
                size: new Vector3(1f, 0.1f, 0.1f)
            );
            List<Vector3> results = new();

            tree.GetElementsInBounds(bounds, results);
            List<Vector3> kdResults = new();
            kd.GetElementsInBounds(bounds, kdResults);
            SpatialAssert.AreEquivalentOrCountEqual(
                kdResults,
                results,
                maxCountForEquivalence: 20000
            );
        }

        [Test]
        public void EdgeAlignedBoundsOnGridConsistentWithKDTree()
        {
            List<Vector3> points = new();
            for (int z = 0; z < 10; ++z)
            {
                for (int y = 0; y < 10; ++y)
                {
                    for (int x = 0; x < 10; ++x)
                    {
                        points.Add(new Vector3(x, y, z));
                    }
                }
            }

            OctTree3D<Vector3> tree = CreateTree(points);
            KdTree3D<Vector3> kd = new(points, p => p);
            Bounds bounds = new(new Vector3(4.5f, 4.5f, 4.5f), new Vector3(9f, 9f, 9f));
            List<Vector3> results = new();
            tree.GetElementsInBounds(bounds, results);
            List<Vector3> kdResults = new();
            kd.GetElementsInBounds(bounds, kdResults);
            SpatialAssert.AreEquivalentOrCountEqual(
                kdResults,
                results,
                maxCountForEquivalence: 20000
            );
        }

        [Test]
        [Timeout(15000)]
        public void FullBoundsOnGridCenteredAtBoundaryCenterConsistentWithKDTree()
        {
            List<Vector3> points = new();
            for (int z = 0; z < 100; ++z)
            {
                for (int y = 0; y < 100; ++y)
                {
                    for (int x = 0; x < 100; ++x)
                    {
                        points.Add(new Vector3(x, y, z));
                    }
                }
            }

            OctTree3D<Vector3> tree = CreateTree(points);
            KdTree3D<Vector3> kd = new(points, p => p);
            Vector3 center = tree.Boundary.center;
            Vector3 size = new(99f, 99f, 99f);
            Bounds bounds = new(center, size);
            List<Vector3> results = new();
            tree.GetElementsInBounds(bounds, results);
            List<Vector3> kdResults = new();
            kd.GetElementsInBounds(bounds, kdResults);
            SpatialAssert.AreEquivalentOrCountEqual(
                kdResults,
                results,
                maxCountForEquivalence: 20000
            );
        }

        [Test]
        public void GetElementsInBoundsTreatsLowerBoundaryAsInclusive()
        {
            List<Vector3> points = new() { new Vector3(-1f, 0f, 0f), new Vector3(0f, 0f, 0f) };

            OctTree3D<Vector3> tree = CreateTree(points);
            Bounds bounds = new(
                center: new Vector3(-0.5f, 0f, 0f),
                size: new Vector3(1f, 0.1f, 0.1f)
            );
            List<Vector3> results = new();

            tree.GetElementsInBounds(bounds, results);

            CollectionAssert.AreEquivalent(points, results);
        }

        [Test]
        public void GetElementsInBoundsIncludesPointExactlyAtUpperBoundary()
        {
            List<Vector3> points = new() { new Vector3(1f, 0f, 0f) };

            OctTree3D<Vector3> tree = CreateTree(points);
            Bounds bounds = new(
                center: new Vector3(0.5f, 0f, 0f),
                size: new Vector3(1f, 0.1f, 0.1f)
            );
            List<Vector3> results = new();

            tree.GetElementsInBounds(bounds, results);

            CollectionAssert.AreEquivalent(points, results);
        }

        [Test]
        public void GetElementsInBoundsWithNoIntersectionReturnsEmptyAdditional()
        {
            List<Vector3> points = new() { new Vector3(-5f, 0f, 0f), new Vector3(5f, 0f, 0f) };

            OctTree3D<Vector3> tree = CreateTree(points);
            Bounds bounds = new(new Vector3(100f, 100f, 100f), new Vector3(2f, 2f, 2f));
            List<Vector3> results = new();

            tree.GetElementsInBounds(bounds, results);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void BoundsDiagnosticsLoggerCapturesVisitsAndPointEvaluations()
        {
            List<Vector3> points = new()
            {
                Vector3.zero,
                new Vector3(1f, 1f, 1f),
                new Vector3(2f, 2f, 2f),
                new Vector3(3f, 3f, 3f),
            };

            OctTree3D<Vector3> tree = CreateTree(points);
            Bounds query = new(new Vector3(1.5f, 1.5f, 1.5f), new Vector3(3f, 3f, 3f));
            OctTreeBoundsQueryDiagnosticsCollector diagnostics = new();
            List<Vector3> results = new();
            tree.GetElementsInBoundsWithDiagnostics(query, results, diagnostics);

            Assert.IsFalse(diagnostics.RootPruned, "Query should intersect root bounds.");
            Assert.Greater(diagnostics.Nodes.Count, 0, "Expected at least one node visit.");
            Assert.Greater(
                diagnostics.Points.Count + diagnostics.BulkAppends.Sum(b => b.AppendedCount),
                0,
                "Diagnostics should record how elements were produced."
            );

            int includedCount =
                diagnostics.Points.Count(p => p.Included)
                + diagnostics.BulkAppends.Sum(record => record.AppendedCount);
            Assert.AreEqual(
                results.Count,
                includedCount,
                "Diagnostics summary should match actual results."
            );
        }

        /// <summary>
        /// An invalid query is the one path a diagnostics logger most wants to see, and it used to
        /// be the one path that returned before the logger was told the query had begun.
        /// </summary>
        [Test]
        public void BoundsDiagnosticsLoggerSeesAnInvalidQuery()
        {
            List<Vector3> points = new() { Vector3.zero, new Vector3(1f, 1f, 1f) };
            OctTree3D<Vector3> tree = CreateTree(points);
            Bounds query = new(Vector3.zero, new Vector3(-2f, -2f, -2f));
            OctTreeBoundsQueryDiagnosticsCollector diagnostics = new();
            List<Vector3> results = new() { new Vector3(9f, 9f, 9f) };

            tree.GetElementsInBoundsWithDiagnostics(query, results, diagnostics);

            Assert.IsEmpty(results);
            Assert.IsTrue(diagnostics.RootPruned, "The logger never saw the query at all.");
            Assert.AreEqual(query, diagnostics.ClosedQuery);
            Assert.IsEmpty(diagnostics.Nodes);
        }

        [Test]
        public void SpatialDiagnosticsReportIncludesOctTreeTrace()
        {
            List<Vector3> points = new() { Vector3.zero, Vector3.one, new Vector3(2f, 2f, 2f) };
            OctTree3D<Vector3> oct = CreateTree(points);
            KdTree3D<Vector3> kd = new(points, p => p);
            Bounds query = new(new Vector3(1f, 1f, 1f), new Vector3(2f, 2f, 2f));

            List<Vector3> kdResults = new();
            kd.GetElementsInBounds(query, kdResults);

            List<Vector3> octResults = new();
            OctTreeBoundsQueryDiagnosticsCollector diagnostics = new();
            oct.GetElementsInBoundsWithDiagnostics(query, octResults, diagnostics);
            if (0 < octResults.Count)
            {
                octResults.RemoveAt(0);
            }

            AssertionException ex = Assert.Throws<AssertionException>(() =>
                SpatialDiagnostics.AssertMatchingResults(
                    "forced mismatch",
                    query,
                    kdResults,
                    octResults,
                    maxItems: 4,
                    octDiagnostics: diagnostics
                )
            );

            StringAssert.Contains("OctTree diagnostics", ex.Message);
            StringAssert.Contains("Node visits", ex.Message);
        }

        [Test]
        public void GetElementsInRangeClearsResultsListAdditional()
        {
            List<Vector3> points = new() { Vector3.zero };
            OctTree3D<Vector3> tree = CreateTree(points);
            Vector3 sentinel = new(999f, 999f, 999f);
            List<Vector3> results = new() { sentinel };

            tree.GetElementsInRange(Vector3.zero, 1f, results);
            Assert.IsFalse(results.Contains(sentinel));
            CollectionAssert.AreEquivalent(points, results);
        }

        [Test]
        public void GetElementsInBoundsClearsResultsListAdditional2()
        {
            List<Vector3> points = new() { Vector3.zero };
            OctTree3D<Vector3> tree = CreateTree(points);
            Vector3 sentinel = new(-999f, -999f, -999f);
            List<Vector3> results = new() { sentinel };

            tree.GetElementsInBounds(new Bounds(Vector3.zero, Vector3.one * 2f), results);
            Assert.IsFalse(results.Contains(sentinel));
            CollectionAssert.AreEquivalent(points, results);
        }

        [Test]
        public void ExtremeCoordinatesHandledCorrectly()
        {
            List<Vector3> points = new()
            {
                new Vector3(float.MinValue / 2f, 0f, 0f),
                new Vector3(float.MaxValue / 2f, 0f, 0f),
                Vector3.zero,
            };

            OctTree3D<Vector3> tree = CreateTree(points);
            List<Vector3> results = new();

            tree.GetElementsInRange(Vector3.zero, float.MaxValue / 2f, results);
            Assert.AreEqual(points.Count, results.Count);
        }

        [Test]
        public void BucketSizeLessThanOneIsClamped()
        {
            List<Vector3> points = new();
            for (int i = 0; i < 64; ++i)
            {
                points.Add(new Vector3(i, i * 0.5f, -i));
            }

            OctTree3D<Vector3> tree = new(points, point => point, bucketSize: 0);

            List<Vector3> results = new();
            tree.GetElementsInRange(Vector3.zero, 500f, results);
            CollectionAssert.AreEquivalent(points, results);
        }
    }
}
