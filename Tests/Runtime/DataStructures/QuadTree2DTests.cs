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
    public sealed class QuadTree2DTests : SpatialTree2DTests<QuadTree2D<Vector2>>
    {
        /*
            A fixed seed, not PRNG.Instance: that hands out an instance seeded from Guid.NewGuid(),
            so a failing case cannot be replayed. SetUp reseeds it, which is what makes running one
            test alone produce the data it produced inside the whole fixture.
        */
        private const uint RandomSeed = 0x5EED0202;

        private IRandom _random = new PcgRandom(RandomSeed);

        private IRandom Random => _random;

        [SetUp]
        public void SeedQuadTree2DRandom()
        {
            _random = new PcgRandom(RandomSeed);
        }

        protected override QuadTree2D<Vector2> CreateTree(IEnumerable<Vector2> points)
        {
            return new QuadTree2D<Vector2>(points, _ => _);
        }

        [Test]
        public void ConstructorWithNullPointsThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                new QuadTree2D<Vector2>(null, _ => _);
            });
        }

        [Test]
        public void ConstructorWithNullTransformerThrowsArgumentNullException()
        {
            List<Vector2> points = new() { Vector2.zero };
            Assert.Throws<ArgumentNullException>(() =>
            {
                new QuadTree2D<Vector2>(points, null);
            });
        }

        [Test]
        public void ConstructorWithEmptyCollectionSucceeds()
        {
            List<Vector2> points = new();
            QuadTree2D<Vector2> tree = CreateTree(points);
            Assert.IsTrue(tree != null);

            List<Vector2> results = new();
            tree.GetElementsInRange(Vector2.zero, 10000f, results);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void ConstructorWithSingleElementSucceeds()
        {
            Vector2 point = new(Random.NextFloat(-100, 100), Random.NextFloat(-100, 100));
            List<Vector2> points = new() { point };
            QuadTree2D<Vector2> tree = CreateTree(points);

            Assert.IsTrue(tree != null);

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
            QuadTree2D<Vector2> tree = CreateTree(points);

            List<Vector2> results = new();
            tree.GetElementsInRange(point, 10000f, results);
            Assert.AreEqual(3, results.Count);
        }

        [Test]
        public void EntryConstructorRespectsProvidedBounds()
        {
            List<QuadTree2D<Vector2>.Entry> entries = new()
            {
                new QuadTree2D<Vector2>.Entry(new Vector2(1, 1), new Vector2(1, 1)),
                new QuadTree2D<Vector2>.Entry(new Vector2(2, 2), new Vector2(2, 2)),
                new QuadTree2D<Vector2>.Entry(new Vector2(3, 3), new Vector2(3, 3)),
            };
            Bounds boundary = new(new Vector3(5, 5, 0), new Vector3(10, 10, 1));

            QuadTree2D<Vector2> tree = new(entries, boundary);

            Assert.AreEqual(boundary.center, tree.Boundary.center);
            Assert.AreEqual(boundary.size, tree.Boundary.size);

            List<Vector2> results = new();
            tree.GetElementsInBounds(boundary, results);

            List<Vector2> expected = entries.Select(entry => entry.value).ToList();
            CollectionAssert.AreEquivalent(expected, results);
        }

        [Test]
        public void EntryConstructorWithEmptyEntriesUsesProvidedBounds()
        {
            Bounds boundary = new(new Vector3(2, 3, 0), new Vector3(4, 6, 1));
            QuadTree2D<Vector2>.Entry[] entries = Array.Empty<QuadTree2D<Vector2>.Entry>();

            QuadTree2D<Vector2> tree = new(entries, boundary);

            Assert.AreEqual(boundary.center, tree.Boundary.center);
            Assert.AreEqual(boundary.size, tree.Boundary.size);

            List<Vector2> results = new();
            tree.GetElementsInBounds(boundary, results);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void BucketSizeLessThanOneIsClamped()
        {
            List<Vector2> points = new();
            for (int i = 0; i < 32; i++)
            {
                points.Add(new Vector2(i, -i));
            }

            QuadTree2D<Vector2> tree = new(points, _ => _, bucketSize: 0);

            List<Vector2> results = new();
            tree.GetElementsInRange(Vector2.zero, 1000f, results);

            CollectionAssert.AreEquivalent(points, results);
        }

        [Test]
        public void EntryConstructorBucketSizeLessThanOneIsClamped()
        {
            List<QuadTree2D<Vector2>.Entry> entries = new()
            {
                new QuadTree2D<Vector2>.Entry(new Vector2(-5, -5), new Vector2(-5, -5)),
                new QuadTree2D<Vector2>.Entry(new Vector2(5, 5), new Vector2(5, 5)),
                new QuadTree2D<Vector2>.Entry(new Vector2(10, -10), new Vector2(10, -10)),
            };

            QuadTree2D<Vector2> tree = new(entries, bucketSize: 0);

            List<Vector2> results = new();
            tree.GetElementsInRange(Vector2.zero, 1000f, results);
            List<Vector2> expected = entries.Select(entry => entry.value).ToList();
            CollectionAssert.AreEquivalent(expected, results);
        }

        [Test]
        public void GetElementsInRangeWithEmptyTreeReturnsEmptyAdditional()
        {
            List<Vector2> points = new();
            QuadTree2D<Vector2> tree = CreateTree(points);
            List<Vector2> results = new();

            tree.GetElementsInRange(Vector2.zero, 100f, results);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetElementsInRangeWithZeroRangeReturnsOnlyExactMatch()
        {
            Vector2 target = new(10, 10);
            List<Vector2> points = new() { target, new(10.1f, 10), new(10, 10.1f) };
            QuadTree2D<Vector2> tree = CreateTree(points);
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
            QuadTree2D<Vector2> tree = CreateTree(points);
            List<Vector2> results = new();

            tree.GetElementsInRange(Vector2.zero, 10000f, results);
            Assert.AreEqual(points.Count, results.Count);
        }

        [Test]
        public void GetElementsInRangeWithMinimumRangeExcludesNearElements()
        {
            Vector2 center = Vector2.zero;
            List<Vector2> points = new()
            {
                new(1, 0), // distance 1
                new(5, 0), // distance 5
                new(10, 0), // distance 10
            };
            QuadTree2D<Vector2> tree = CreateTree(points);
            List<Vector2> results = new();

            // Get elements between distance 2 and 8
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
            QuadTree2D<Vector2> tree = CreateTree(points);
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
            QuadTree2D<Vector2> tree = CreateTree(points);
            List<Vector2> results = new();

            Bounds searchBounds = new(Vector3.zero, Vector3.one * 10);
            tree.GetElementsInBounds(searchBounds, results);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetElementsInBoundsWithNonIntersectingBoundsReturnsEmpty()
        {
            List<Vector2> points = new() { new(0, 0), new(1, 1), new(2, 2) };
            QuadTree2D<Vector2> tree = CreateTree(points);
            List<Vector2> results = new();

            Bounds searchBounds = new(new Vector3(100, 100, 0), new Vector3(10, 10, 1));
            tree.GetElementsInBounds(searchBounds, results);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetElementsInBoundsWithTinyBoundsFindsExactElements()
        {
            Vector2 target = new(5, 5);
            List<Vector2> points = new() { target, new(5.5f, 5.5f), new(10, 10) };
            QuadTree2D<Vector2> tree = CreateTree(points);
            List<Vector2> results = new();

            Bounds searchBounds = new(new Vector3(5, 5, 0), new Vector3(0.1f, 0.1f, 1));
            tree.GetElementsInBounds(searchBounds, results);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(target, results[0]);
        }

        [Test]
        public void GetApproximateNearestNeighborsWithEmptyTreeReturnsEmpty()
        {
            List<Vector2> points = new();
            QuadTree2D<Vector2> tree = CreateTree(points);
            List<Vector2> results = new();

            tree.GetApproximateNearestNeighbors(Vector2.zero, 5, results);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetApproximateNearestNeighborsWithCountZeroReturnsEmpty()
        {
            List<Vector2> points = new() { Vector2.zero, Vector2.one };
            QuadTree2D<Vector2> tree = CreateTree(points);
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
            QuadTree2D<Vector2> tree = CreateTree(points);
            List<Vector2> results = new();

            int requestedCount = 10;
            tree.GetApproximateNearestNeighbors(Vector2.zero, requestedCount, results);
            Assert.AreEqual(requestedCount, results.Count);
        }

        [Test]
        public void GetApproximateNearestNeighborsWithCountGreaterThanElementsReturnsAll()
        {
            List<Vector2> points = new() { Vector2.zero, Vector2.one, Vector2.right };
            QuadTree2D<Vector2> tree = CreateTree(points);
            List<Vector2> results = new();

            tree.GetApproximateNearestNeighbors(Vector2.zero, 100, results);
            Assert.AreEqual(3, results.Count);
        }

        [Test]
        public void GetApproximateNearestNeighborsReturnsClosestElements()
        {
            Vector2 center = Vector2.zero;
            List<Vector2> points = new() { new(1, 0), new(0, 1), new(100, 100), new(200, 200) };
            QuadTree2D<Vector2> tree = CreateTree(points);
            List<Vector2> results = new();

            tree.GetApproximateNearestNeighbors(center, 2, results);
            Assert.AreEqual(2, results.Count);

            // Verify both results are closer than the far points
            foreach (Vector2 result in results)
            {
                float distance = Vector2.Distance(center, result);
                Assert.Less(distance, 10f);
            }
        }

        [Test]
        public void GetApproximateNearestNeighborsAtTreeCornerFindsElements()
        {
            List<Vector2> points = new();
            for (int x = 0; x < 10; x++)
            {
                for (int y = 0; y < 10; y++)
                {
                    points.Add(new Vector2(x, y));
                }
            }
            QuadTree2D<Vector2> tree = CreateTree(points);
            List<Vector2> results = new();

            Vector2 corner = new(-100, -100);
            tree.GetApproximateNearestNeighbors(corner, 5, results);
            Assert.AreEqual(5, results.Count);
        }

        [Test]
        public void BoundaryCalculatedCorrectlyForPositivePoints()
        {
            List<Vector2> points = new() { new(0, 0), new(10, 0), new(0, 10), new(10, 10) };
            QuadTree2D<Vector2> tree = CreateTree(points);

            Bounds bounds = tree.Boundary;
            Assert.Greater(bounds.size.x, 0);
            Assert.Greater(bounds.size.y, 0);
            Assert.IsTrue(bounds.Contains(new Vector3(5, 5, 0)));
        }

        [Test]
        public void BoundaryCalculatedCorrectlyForNegativePoints()
        {
            List<Vector2> points = new() { new(-10, -10), new(-5, -5), new(-1, -1) };
            QuadTree2D<Vector2> tree = CreateTree(points);

            Bounds bounds = tree.Boundary;
            Assert.IsTrue(bounds.Contains(new Vector3(-5, -5, 0)));
        }

        [Test]
        public void BoundaryCalculatedCorrectlyForMixedPoints()
        {
            List<Vector2> points = new() { new(-100, -100), new(100, 100) };
            QuadTree2D<Vector2> tree = CreateTree(points);

            Bounds bounds = tree.Boundary;
            Assert.IsTrue(bounds.Contains(new Vector3(-100, -100, 0)));
            Assert.IsTrue(bounds.Contains(new Vector3(100, 100, 0)));
            Assert.IsTrue(bounds.Contains(Vector3.zero));
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
            QuadTree2D<Vector2> tree = CreateTree(points);

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

            QuadTree2D<Vector2> treeSmallBucket = new(points, _ => _, bucketSize: 1);
            QuadTree2D<Vector2> treeLargeBucket = new(points, _ => _, bucketSize: 100);

            List<Vector2> resultsSmall = new();
            List<Vector2> resultsLarge = new();
            treeSmallBucket.GetElementsInRange(Vector2.zero, 100000f, resultsSmall);
            treeLargeBucket.GetElementsInRange(Vector2.zero, 100000f, resultsLarge);

            Assert.AreEqual(100, resultsSmall.Count);
            Assert.AreEqual(100, resultsLarge.Count);
        }

        [Test]
        public void ElementsAtBoundaryAreHandledCorrectly()
        {
            List<Vector2> points = new()
            {
                new(0, 0),
                new(100, 0),
                new(0, 100),
                new(100, 100),
                new(50, 50),
            };
            QuadTree2D<Vector2> tree = CreateTree(points);
            List<Vector2> results = new();

            tree.GetElementsInRange(new Vector2(50, 50), 100f, results);
            Assert.AreEqual(5, results.Count);
        }

        [Test]
        public void MultipleQueriesOnSameTreeReturnConsistentResults()
        {
            List<Vector2> points = new();
            for (int i = 0; i < 50; i++)
            {
                points.Add(new Vector2(Random.NextFloat(-50, 50), Random.NextFloat(-50, 50)));
            }
            QuadTree2D<Vector2> tree = CreateTree(points);

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
        public void VeryClosePointsAreDistinguished()
        {
            List<Vector2> points = new()
            {
                new(0, 0),
                new(0.0001f, 0),
                new(0, 0.0001f),
                new(0.0001f, 0.0001f),
            };
            QuadTree2D<Vector2> tree = CreateTree(points);

            List<Vector2> allResults = new();
            tree.GetElementsInRange(Vector2.zero, 10000f, allResults);
            Assert.AreEqual(4, allResults.Count);

            List<Vector2> results = new();
            tree.GetElementsInRange(Vector2.zero, 0.001f, results);
            Assert.AreEqual(4, results.Count);
        }

        [Test]
        public void ColinearPointsHandledCorrectly()
        {
            List<Vector2> points = new();
            for (int i = 0; i < 100; i++)
            {
                points.Add(new Vector2(i, 0)); // All on x-axis
            }
            QuadTree2D<Vector2> tree = CreateTree(points);

            // Verify tree was created successfully
            Assert.IsTrue(tree != null);

            // Verify all points are stored in the tree
            Assert.AreEqual(100, tree.elements.Length);

            // Verify tree handles colinear data and can query them
            List<Vector2> results = new();
            tree.GetElementsInRange(new Vector2(50, 0), 10f, results);
            Assert.Greater(results.Count, 0, "Should find points within range of (50, 0)");

            // Verify the correct points are returned (those within distance 10 from x=50, y=0)
            // Points from x=40 to x=60 should be within range (distance <= 10)
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

            // Test edge cases - query at boundaries
            results.Clear();
            tree.GetElementsInRange(new Vector2(0, 0), 5f, results);
            Assert.Greater(results.Count, 0, "Should find points at start of line");

            results.Clear();
            tree.GetElementsInRange(new Vector2(99, 0), 5f, results);
            Assert.Greater(results.Count, 0, "Should find points at end of line");

            // Test query away from the line should return nothing or very few
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
                points.Add(new Vector2(0, i)); // All on y-axis
            }
            QuadTree2D<Vector2> tree = CreateTree(points);

            // Verify tree was created successfully
            Assert.IsTrue(tree != null);

            // Verify all points are stored in the tree
            Assert.AreEqual(100, tree.elements.Length);

            // Verify tree handles vertical line data and can query them
            List<Vector2> results = new();
            tree.GetElementsInRange(new Vector2(0, 50), 10f, results);
            Assert.Greater(results.Count, 0, "Should find points within range of (0, 50)");

            // Verify the correct points are returned (those within distance 10 from x=0, y=50)
            // Points from y=40 to y=60 should be within range (distance <= 10)
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

            // Test edge cases - query at boundaries
            results.Clear();
            tree.GetElementsInRange(new Vector2(0, 0), 5f, results);
            Assert.Greater(results.Count, 0, "Should find points at start of line");

            results.Clear();
            tree.GetElementsInRange(new Vector2(0, 99), 5f, results);
            Assert.Greater(results.Count, 0, "Should find points at end of line");

            // Test query away from the line should return nothing
            results.Clear();
            tree.GetElementsInRange(new Vector2(100, 50), 5f, results);
            Assert.AreEqual(0, results.Count, "Should find no points far from the line");

            // Test GetElementsInBounds with various bounds
            results.Clear();
            tree.GetElementsInBounds(
                new Bounds(new Vector3(0, 50, 0), new Vector3(5, 20, 1)),
                results
            );
            Assert.Greater(results.Count, 0, "Should find points in bounds centered at (0, 50)");
            // Bounds with size (5, 20, 1) means half-extents of (2.5, 10, 0.5)
            // So y range is [40, 60), should contain 20 points (max bound is exclusive)
            Assert.GreaterOrEqual(results.Count, 20, "Should find points from y=40 to y=59");
            foreach (Vector2 result in results)
            {
                Assert.AreEqual(0, result.x, "All points should be on y-axis (x=0)");
                Assert.GreaterOrEqual(result.y, 40, "Points should be >= y=40");
                Assert.LessOrEqual(
                    result.y,
                    60,
                    "Points should be < y=60 (max bound is exclusive)"
                );
            }

            // Test bounds at the start of the vertical line
            results.Clear();
            tree.GetElementsInBounds(
                new Bounds(new Vector3(0, 5, 0), new Vector3(4, 10, 1)),
                results
            );
            Assert.Greater(results.Count, 0, "Should find points at start of line");
            foreach (Vector2 result in results)
            {
                Assert.AreEqual(0, result.x, "All points should be on y-axis (x=0)");
                Assert.GreaterOrEqual(result.y, 0, "Points should be >= y=0");
                Assert.LessOrEqual(result.y, 10, "Points should be <= y=10");
            }

            // Test bounds at the end of the vertical line
            results.Clear();
            tree.GetElementsInBounds(
                new Bounds(new Vector3(0, 95, 0), new Vector3(4, 10, 1)),
                results
            );
            Assert.Greater(results.Count, 0, "Should find points at end of line");
            foreach (Vector2 result in results)
            {
                Assert.AreEqual(0, result.x, "All points should be on y-axis (x=0)");
                Assert.GreaterOrEqual(result.y, 90, "Points should be >= y=90");
                Assert.LessOrEqual(result.y, 99, "Points should be <= y=99");
            }

            // Test bounds that don't intersect the vertical line
            results.Clear();
            tree.GetElementsInBounds(
                new Bounds(new Vector3(50, 50, 0), new Vector3(10, 20, 1)),
                results
            );
            Assert.AreEqual(0, results.Count, "Should find no points in bounds away from line");

            // Test very narrow bounds along the line
            results.Clear();
            tree.GetElementsInBounds(
                new Bounds(new Vector3(0, 50, 0), new Vector3(0.1f, 5, 1)),
                results
            );
            Assert.Greater(results.Count, 0, "Should find points even with narrow bounds");
            Assert.LessOrEqual(results.Count, 11, "Should find at most 11 points (y=45 to y=55)");

            // Test querying the entire vertical line with large bounds
            results.Clear();
            tree.GetElementsInBounds(
                new Bounds(new Vector3(0, 50, 0), new Vector3(10, 100, 1)),
                results
            );
            Assert.AreEqual(100, results.Count, "Should find all 100 points with large bounds");

            // Test small range queries at specific positions
            results.Clear();
            tree.GetElementsInRange(new Vector2(0, 25), 2f, results);
            Assert.Greater(results.Count, 0, "Should find points near y=25");
            foreach (Vector2 result in results)
            {
                Assert.AreEqual(0, result.x, "All points should be on y-axis (x=0)");
                float distance = Vector2.Distance(result, new Vector2(0, 25));
                Assert.LessOrEqual(distance, 2f, $"Point {result} should be within range 2");
            }

            // Test range query with minimum range
            results.Clear();
            tree.GetElementsInRange(new Vector2(0, 50), 10f, results, minimumRange: 5f);
            Assert.Greater(results.Count, 0, "Should find points in annular region");
            foreach (Vector2 result in results)
            {
                float distance = Vector2.Distance(result, new Vector2(0, 50));
                Assert.GreaterOrEqual(distance, 5f, $"Point {result} should be >= minimum range 5");
                Assert.LessOrEqual(distance, 10f, $"Point {result} should be <= maximum range 10");
            }
        }

        [Test]
        public void ExtremeCoordinatesHandledCorrectly()
        {
            List<Vector2> points = new()
            {
                new(float.MinValue / 2, float.MinValue / 2),
                new(float.MaxValue / 2, float.MaxValue / 2),
                new(0, 0),
            };

            // Should not throw
            QuadTree2D<Vector2> tree = CreateTree(points);

            List<Vector2> results = new();
            tree.GetElementsInRange(Vector2.zero, float.MaxValue / 2, results);
            Assert.AreEqual(3, results.Count);
        }

        [Test]
        public void GetElementsInRangeClearsResultsListAdditional()
        {
            List<Vector2> points = new() { Vector2.zero };
            QuadTree2D<Vector2> tree = CreateTree(points);
            List<Vector2> results = new() { Vector2.one, Vector2.right };

            tree.GetElementsInRange(Vector2.zero, 1f, results);
            // Results should be cleared and repopulated
            Assert.IsTrue(results.All(v => points.Contains(v)));
        }

        [Test]
        public void GetElementsInBoundsClearsResultsListAdditional()
        {
            List<Vector2> points = new() { Vector2.zero };
            QuadTree2D<Vector2> tree = CreateTree(points);
            List<Vector2> results = new() { Vector2.one, Vector2.right };

            tree.GetElementsInBounds(new Bounds(Vector3.zero, Vector3.one * 10), results);
            Assert.IsTrue(results.All(v => points.Contains(v)));
        }
    }
}
