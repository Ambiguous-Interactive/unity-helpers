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

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class UnbalancedKdTree2DTests : SpatialTree2DTests<KdTree2D<Vector2>>
    {
        // Reseed each test so a failing tree can be reproduced alone or within the fixture.
        private const uint RandomSeed = 0x5EED0205;

        private IRandom _random = new PcgRandom(RandomSeed);

        private IRandom Random => _random;

        [SetUp]
        public void SeedUnbalancedKdTree2DRandom()
        {
            _random = new PcgRandom(RandomSeed);
        }

        protected override KdTree2D<Vector2> CreateTree(IEnumerable<Vector2> points)
        {
            return new KdTree2D<Vector2>(points, _ => _, balanced: false);
        }

        [Test]
        public void ConstructorWithNullPointsThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                new KdTree2D<Vector2>(null, _ => _, balanced: false);
            });
        }

        [Test]
        public void ConstructorWithNullTransformerThrowsArgumentNullException()
        {
            List<Vector2> points = new() { Vector2.zero };
            Assert.Throws<ArgumentNullException>(() =>
            {
                new KdTree2D<Vector2>(points, null, balanced: false);
            });
        }

        [Test]
        public void ConstructorWithEmptyCollectionSucceeds()
        {
            List<Vector2> points = new();
            KdTree2D<Vector2> tree = CreateTree(points);
            Assert.IsTrue(
                tree != null,
                "KdTree should be created successfully for empty collection"
            );

            List<Vector2> results = new();
            tree.GetElementsInRange(Vector2.zero, 10000f, results);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void ConstructorWithSingleElementSucceeds()
        {
            Vector2 point = new(Random.NextFloat(-100, 100), Random.NextFloat(-100, 100));
            List<Vector2> points = new() { point };
            KdTree2D<Vector2> tree = CreateTree(points);

            Assert.IsTrue(tree != null, "KdTree should be created successfully for single element");

            List<Vector2> results = new();
            tree.GetElementsInRange(point, 10000f, results);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(point, results[0]);
        }

        [Test]
        public void ConstructorWithDuplicateElementsPreservesAll()
        {
            Vector2 point = new(5, 5);
            List<Vector2> points = new() { point, point, point };
            KdTree2D<Vector2> tree = CreateTree(points);

            List<Vector2> results = new();
            tree.GetElementsInRange(point, 10000f, results);
            Assert.AreEqual(3, results.Count);
        }

        [Test]
        public void GetElementsInRangeWithEmptyTreeReturnsEmptyAdditional()
        {
            List<Vector2> points = new();
            KdTree2D<Vector2> tree = CreateTree(points);
            List<Vector2> results = new();

            tree.GetElementsInRange(Vector2.zero, 100f, results);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetElementsInRangeWithZeroRangeReturnsOnlyExactMatch()
        {
            Vector2 target = new(10, 10);
            List<Vector2> points = new() { target, new(10.1f, 10), new(10, 10.1f) };
            KdTree2D<Vector2> tree = CreateTree(points);
            List<Vector2> results = new();

            tree.GetElementsInRange(target, 0f, results);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(target, results[0]);
        }

        [Test]
        public void GetElementsInRangeWithVeryLargeRangeReturnsAll()
        {
            List<Vector2> points = new();
            for (int i = 0; i < 100; i++)
            {
                points.Add(new Vector2(Random.NextFloat(-50, 50), Random.NextFloat(-50, 50)));
            }
            KdTree2D<Vector2> tree = CreateTree(points);
            List<Vector2> results = new();

            tree.GetElementsInRange(Vector2.zero, 10000f, results);
            Assert.AreEqual(points.Count, results.Count);
        }

        [Test]
        public void GetElementsInRangeWithMinimumRangeExcludesNearElements()
        {
            Vector2 center = Vector2.zero;
            List<Vector2> points = new() { new(1, 0), new(5, 0), new(10, 0) };
            KdTree2D<Vector2> tree = CreateTree(points);
            List<Vector2> results = new();

            tree.GetElementsInRange(center, 8f, results, minimumRange: 2f);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(new Vector2(5, 0), results[0]);
        }

        [Test]
        public void GetElementsInBoundsReturnsElementsWithinBounds()
        {
            List<Vector2> points = new();
            for (int x = 0; x < 10; x++)
            {
                for (int y = 0; y < 10; y++)
                {
                    points.Add(new Vector2(x, y));
                }
            }
            KdTree2D<Vector2> tree = CreateTree(points);
            List<Vector2> results = new();

            Bounds searchBounds = new(new Vector3(5, 5, 0), new Vector3(3, 3, 1));
            tree.GetElementsInBounds(searchBounds, results);

            Assert.Greater(results.Count, 0);
            foreach (Vector2 result in results)
            {
                Assert.IsTrue(searchBounds.Contains(result));
            }
        }

        [Test]
        public void GetElementsInBoundsWithEmptyTreeReturnsEmpty()
        {
            List<Vector2> points = new();
            KdTree2D<Vector2> tree = CreateTree(points);
            List<Vector2> results = new();

            Bounds searchBounds = new(Vector3.zero, Vector3.one * 10);
            tree.GetElementsInBounds(searchBounds, results);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetElementsInBoundsWithNonIntersectingBoundsReturnsEmpty()
        {
            List<Vector2> points = new() { new(0, 0), new(1, 1), new(2, 2) };
            KdTree2D<Vector2> tree = CreateTree(points);
            List<Vector2> results = new();

            Bounds searchBounds = new(new Vector3(100, 100, 0), new Vector3(10, 10, 1));
            tree.GetElementsInBounds(searchBounds, results);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetApproximateNearestNeighborsWithEmptyTreeReturnsEmpty()
        {
            List<Vector2> points = new();
            KdTree2D<Vector2> tree = CreateTree(points);
            List<Vector2> results = new();

            tree.GetApproximateNearestNeighbors(Vector2.zero, 5, results);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetApproximateNearestNeighborsWithCountZeroReturnsEmpty()
        {
            List<Vector2> points = new() { Vector2.zero, Vector2.one };
            KdTree2D<Vector2> tree = CreateTree(points);
            List<Vector2> results = new();

            tree.GetApproximateNearestNeighbors(Vector2.zero, 0, results);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetApproximateNearestNeighborsReturnsRequestedCount()
        {
            List<Vector2> points = new();
            for (int i = 0; i < 50; i++)
            {
                points.Add(new Vector2(Random.NextFloat(-100, 100), Random.NextFloat(-100, 100)));
            }
            KdTree2D<Vector2> tree = CreateTree(points);
            List<Vector2> results = new();

            int requestedCount = 10;
            tree.GetApproximateNearestNeighbors(Vector2.zero, requestedCount, results);
            Assert.AreEqual(requestedCount, results.Count);
        }

        [Test]
        public void GetApproximateNearestNeighborsWithCountGreaterThanElementsReturnsAll()
        {
            List<Vector2> points = new() { Vector2.zero, Vector2.one, Vector2.right };
            KdTree2D<Vector2> tree = CreateTree(points);
            List<Vector2> results = new();

            tree.GetApproximateNearestNeighbors(Vector2.zero, 100, results);
            Assert.AreEqual(3, results.Count);
        }

        [Test]
        public void UnbalancedTreeHandlesSortedInput()
        {
            List<Vector2> points = new();
            for (int i = 0; i < 100; i++)
            {
                points.Add(new Vector2(i, i));
            }
            KdTree2D<Vector2> tree = CreateTree(points);

            List<Vector2> results = new();
            tree.GetElementsInRange(new Vector2(50, 50), 10f, results);
            Assert.Greater(results.Count, 0);
        }

        [Test]
        public void BoundaryCalculatedCorrectlyForPositivePoints()
        {
            List<Vector2> points = new() { new(0, 0), new(10, 0), new(0, 10), new(10, 10) };
            KdTree2D<Vector2> tree = CreateTree(points);

            Bounds bounds = tree.Boundary;
            Assert.Greater(bounds.size.x, 0);
            Assert.Greater(bounds.size.y, 0);
            Assert.IsTrue(bounds.Contains(new Vector3(5, 5, 0)));
        }

        [Test]
        public void BoundaryCalculatedCorrectlyForNegativePoints()
        {
            List<Vector2> points = new() { new(-10, -10), new(-5, -5), new(-1, -1) };
            KdTree2D<Vector2> tree = CreateTree(points);

            Bounds bounds = tree.Boundary;
            Assert.IsTrue(bounds.Contains(new Vector3(-5, -5, 0)));
        }

        [Test]
        public void LargeDatasetStressTest()
        {
            List<Vector2> points = new();
            for (int i = 0; i < 10000; i++)
            {
                points.Add(
                    new Vector2(Random.NextFloat(-1000, 1000), Random.NextFloat(-1000, 1000))
                );
            }
            KdTree2D<Vector2> tree = CreateTree(points);

            List<Vector2> allResults = new();
            tree.GetElementsInRange(Vector2.zero, 100000f, allResults);
            Assert.AreEqual(10000, allResults.Count);

            List<Vector2> results = new();
            tree.GetElementsInRange(Vector2.zero, 50f, results);
            Assert.GreaterOrEqual(results.Count, 0);
        }

        [Test]
        public void CustomBucketSizeAffectsTreeStructure()
        {
            List<Vector2> points = new();
            for (int i = 0; i < 100; i++)
            {
                points.Add(new Vector2(i, i));
            }

            KdTree2D<Vector2> treeSmallBucket = new(points, v => v, bucketSize: 1, balanced: false);
            KdTree2D<Vector2> treeLargeBucket = new(
                points,
                v => v,
                bucketSize: 100,
                balanced: false
            );

            List<Vector2> resultsSmall = new();
            List<Vector2> resultsLarge = new();
            treeSmallBucket.GetElementsInRange(Vector2.zero, 100000f, resultsSmall);
            treeLargeBucket.GetElementsInRange(Vector2.zero, 100000f, resultsLarge);

            Assert.AreEqual(100, resultsSmall.Count);
            Assert.AreEqual(100, resultsLarge.Count);
        }

        [Test]
        public void ColinearPointsHandledCorrectly()
        {
            List<Vector2> points = new();
            for (int i = 0; i < 100; i++)
            {
                points.Add(new Vector2(i, 0));
            }
            KdTree2D<Vector2> tree = CreateTree(points);

            Assert.IsTrue(tree != null);

            Assert.AreEqual(100, tree.elements.Length);

            List<Vector2> results = new();
            tree.GetElementsInRange(new Vector2(50, 0), 10f, results);
            Assert.Greater(results.Count, 0, "Should find points within range of (50, 0)");

            Assert.GreaterOrEqual(
                results.Count,
                21,
                "Should find at least points from x=40 to x=60"
            );
            foreach (Vector2 result in results)
            {
                float distance = Vector2.Distance(result, new Vector2(50, 0));
                Assert.LessOrEqual(distance, 10f, $"Point {result} should be within range 10");
                Assert.AreEqual(0, result.y, "All points should be on x-axis (y=0)");
            }

            results.Clear();
            tree.GetElementsInRange(new Vector2(0, 0), 5f, results);
            Assert.Greater(results.Count, 0, "Should find points at start of line");

            results.Clear();
            tree.GetElementsInRange(new Vector2(99, 0), 5f, results);
            Assert.Greater(results.Count, 0, "Should find points at end of line");

            results.Clear();
            tree.GetElementsInRange(new Vector2(50, 100), 5f, results);
            Assert.AreEqual(0, results.Count, "Should find no points far from the line");
        }

        [Test]
        public void SingleLineVerticalPointsHandledCorrectly()
        {
            List<Vector2> points = new();
            for (int i = 0; i < 100; i++)
            {
                points.Add(new Vector2(0, i));
            }
            KdTree2D<Vector2> tree = CreateTree(points);

            Assert.IsTrue(tree != null);

            Assert.AreEqual(100, tree.elements.Length);

            List<Vector2> results = new();
            tree.GetElementsInRange(new Vector2(0, 50), 10f, results);
            Assert.Greater(results.Count, 0, "Should find points within range of (0, 50)");

            Assert.GreaterOrEqual(
                results.Count,
                21,
                "Should find at least points from y=40 to y=60"
            );
            foreach (Vector2 result in results)
            {
                float distance = Vector2.Distance(result, new Vector2(0, 50));
                Assert.LessOrEqual(distance, 10f, $"Point {result} should be within range 10");
                Assert.AreEqual(0, result.x, "All points should be on y-axis (x=0)");
            }

            results.Clear();
            tree.GetElementsInRange(new Vector2(0, 0), 5f, results);
            Assert.Greater(results.Count, 0, "Should find points at start of line");

            results.Clear();
            tree.GetElementsInRange(new Vector2(0, 99), 5f, results);
            Assert.Greater(results.Count, 0, "Should find points at end of line");

            results.Clear();
            tree.GetElementsInRange(new Vector2(100, 50), 5f, results);
            Assert.AreEqual(0, results.Count, "Should find no points far from the line");

            results.Clear();
            tree.GetElementsInBounds(
                new Bounds(new Vector3(0, 50, 0), new Vector3(5, 20, 1)),
                results
            );
            Assert.Greater(results.Count, 0, "Should find points in bounds centered at (0, 50)");

            Assert.GreaterOrEqual(results.Count, 20, "Should find points from y=40 to y=59");
            foreach (Vector2 result in results)
            {
                Assert.AreEqual(0, result.x, "All points should be on y-axis (x=0)");
                Assert.GreaterOrEqual(result.y, 40f, "Points should be >= y=40");
                Assert.LessOrEqual(result.y, 60f, "Points should be <= y=60");
            }
        }

        [Test]
        public void VeryClosePointsAreDistinguished()
        {
            List<Vector2> points = new()
            {
                new(0, 0),
                new(0.0001f, 0),
                new(0, 0.0001f),
                new(0.0001f, 0.0001f),
            };
            KdTree2D<Vector2> tree = CreateTree(points);

            List<Vector2> results = new();
            tree.GetElementsInRange(Vector2.zero, 0.001f, results);
            Assert.AreEqual(4, results.Count);
        }

        [Test]
        public void MultipleQueriesOnSameTreeReturnConsistentResults()
        {
            List<Vector2> points = new();
            for (int i = 0; i < 50; i++)
            {
                points.Add(new Vector2(Random.NextFloat(-50, 50), Random.NextFloat(-50, 50)));
            }
            KdTree2D<Vector2> tree = CreateTree(points);

            List<Vector2> results1 = new();
            List<Vector2> results2 = new();

            Vector2 queryPoint = Vector2.zero;
            float queryRange = 25f;

            tree.GetElementsInRange(queryPoint, queryRange, results1);
            tree.GetElementsInRange(queryPoint, queryRange, results2);

            Assert.AreEqual(results1.Count, results2.Count);
            CollectionAssert.AreEquivalent(results1, results2);
        }

        [Test]
        public void GetElementsInRangeClearsResultsListAdditional()
        {
            List<Vector2> points = new() { Vector2.zero };
            KdTree2D<Vector2> tree = CreateTree(points);
            List<Vector2> results = new() { Vector2.one, Vector2.right };

            tree.GetElementsInRange(Vector2.zero, 1f, results);
            Assert.IsTrue(results.All(v => points.Contains(v)));
        }

        [Test]
        public void GetElementsInBoundsClearsResultsListAdditional()
        {
            List<Vector2> points = new() { Vector2.zero };
            KdTree2D<Vector2> tree = CreateTree(points);
            List<Vector2> results = new() { Vector2.one, Vector2.right };

            tree.GetElementsInBounds(new Bounds(Vector3.zero, Vector3.one * 10), results);
            Assert.IsTrue(results.All(v => points.Contains(v)));
        }

        [Test]
        public void WorstCaseSequentialInsertionStillWorks()
        {
            List<Vector2> points = new();
            for (int i = 0; i < 1000; i++)
            {
                points.Add(new Vector2(i, 0));
            }

            KdTree2D<Vector2> tree = CreateTree(points);

            List<Vector2> results = new();
            tree.GetElementsInRange(new Vector2(500, 0), 50f, results);
            Assert.Greater(results.Count, 0);
        }

        [Test]
        public void ReverseSortedInputHandledCorrectly()
        {
            List<Vector2> points = new();
            for (int i = 100; 0 <= i; i--)
            {
                points.Add(new Vector2(i, i));
            }

            KdTree2D<Vector2> tree = CreateTree(points);

            List<Vector2> results = new();
            tree.GetElementsInRange(new Vector2(50, 50), 10f, results);
            Assert.Greater(results.Count, 0);
        }

        [Test]
        public void RandomOrderInputProducesCorrectResults()
        {
            List<Vector2> points = new();
            for (int i = 0; i < 100; i++)
            {
                points.Add(new Vector2(Random.NextFloat(-100, 100), Random.NextFloat(-100, 100)));
            }

            KdTree2D<Vector2> tree = CreateTree(points);

            List<Vector2> allResults = new();
            tree.GetElementsInRange(Vector2.zero, 10000f, allResults);
            Assert.AreEqual(100, allResults.Count);
        }

        [Test]
        public void PathologicalDataPatternHandledCorrectly()
        {
            List<Vector2> points = new();
            for (int i = 0; i < 50; i++)
            {
                points.Add(new Vector2(i, 0));
                points.Add(new Vector2(0, i));
            }

            KdTree2D<Vector2> tree = CreateTree(points);

            List<Vector2> results = new();
            tree.GetElementsInRange(Vector2.zero, 10f, results);
            Assert.Greater(results.Count, 0);
        }
    }
}
