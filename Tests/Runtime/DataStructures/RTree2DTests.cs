// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Random;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class RTree2DTests : SpatialTree2DTests<RTree2D<Vector2>>
    {
        // Reseed each test so a failing tree can be reproduced alone or within the fixture.
        private const uint RandomSeed = 0x5EED0203;

        private IRandom _random = new PcgRandom(RandomSeed);

        private IRandom Random => _random;

        [SetUp]
        public void SeedRTree2DRandom()
        {
            _random = new PcgRandom(RandomSeed);
        }

        protected override RTree2D<Vector2> CreateTree(IEnumerable<Vector2> points)
        {
            return new RTree2D<Vector2>(points, CreatePointBounds);
        }

        private static Bounds CreatePointBounds(Vector2 point)
        {
            const float pointBoundsSize = 0.001f;
            return new Bounds(
                new Vector3(point.x, point.y, 0f),
                new Vector3(pointBoundsSize, pointBoundsSize, pointBoundsSize)
            );
        }

        private RTree2D<Bounds> CreateBoundsTree(IEnumerable<Bounds> bounds)
        {
            return new RTree2D<Bounds>(bounds, b => b);
        }

        private static List<Bounds> QueryBounds(RTree2D<Bounds> tree, Bounds bounds)
        {
            List<Bounds> results = new();
            tree.GetElementsInBounds(bounds, results);
            return results;
        }

        private static List<Bounds> QueryRange(
            RTree2D<Bounds> tree,
            Vector2 position,
            float range,
            float minimumRange = 0f
        )
        {
            List<Bounds> results = new();
            tree.GetElementsInRange(position, range, results, minimumRange);
            return results;
        }

        [Test]
        public void ConstructorWithNullPointsThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                new RTree2D<Bounds>(null, b => b);
            });
        }

        [Test]
        public void ConstructorWithNullTransformerThrowsArgumentNullException()
        {
            List<Bounds> bounds = new() { new Bounds(Vector3.zero, Vector3.one) };
            Assert.Throws<ArgumentNullException>(() =>
            {
                new RTree2D<Bounds>(bounds, null);
            });
        }

        [Test]
        public void ConstructorWithEmptyCollectionSucceeds()
        {
            List<Bounds> bounds = new();
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);
            Assert.IsTrue(tree != null);

            List<Bounds> results = QueryBounds(tree, new Bounds(Vector3.zero, Vector3.one * 1000));
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void ConstructorWithSingleElementSucceeds()
        {
            Bounds bound = new(
                new Vector3(Random.NextFloat(-100, 100), Random.NextFloat(-100, 100), 0),
                Vector3.one * 10
            );
            List<Bounds> bounds = new() { bound };
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);

            Assert.IsTrue(tree != null);

            List<Bounds> results = QueryBounds(tree, new Bounds(Vector3.zero, Vector3.one * 1000));
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(bound, results[0]);
        }

        [Test]
        public void ConstructorWithDuplicateElementsPreservesAll()
        {
            Bounds bound = new(new Vector3(5, 5, 0), Vector3.one * 2);
            List<Bounds> bounds = new() { bound, bound, bound };
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);

            List<Bounds> results = QueryBounds(tree, new Bounds(Vector3.zero, Vector3.one * 1000));
            Assert.AreEqual(3, results.Count);
        }

        [Test]
        public void GetElementsInBoundsWithEmptyTreeReturnsEmpty()
        {
            List<Bounds> bounds = new();
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);

            List<Bounds> results = QueryBounds(tree, new Bounds(Vector3.zero, Vector3.one * 100));
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetElementsInBoundsReturnsIntersectingElements()
        {
            List<Bounds> bounds = new();
            for (int x = 0; x < 10; x++)
            {
                for (int y = 0; y < 10; y++)
                {
                    bounds.Add(new Bounds(new Vector3(x * 10, y * 10, 0), Vector3.one * 5));
                }
            }
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);

            Bounds searchBounds = new(new Vector3(50, 50, 0), Vector3.one * 30);
            List<Bounds> results = QueryBounds(tree, searchBounds);

            Assert.Greater(results.Count, 0);
            foreach (Bounds result in results)
            {
                Assert.IsTrue(searchBounds.Intersects(result));
            }
        }

        [Test]
        public void GetElementsInBoundsWithNonIntersectingBoundsReturnsEmpty()
        {
            List<Bounds> bounds = new()
            {
                new(new Vector3(0, 0, 0), Vector3.one),
                new(new Vector3(10, 10, 0), Vector3.one),
                new(new Vector3(20, 20, 0), Vector3.one),
            };
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);

            Bounds searchBounds = new(new Vector3(1000, 1000, 0), Vector3.one * 10);
            List<Bounds> results = QueryBounds(tree, searchBounds);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetElementsInBoundsWithTinyBoundsFindsIntersecting()
        {
            Bounds target = new(new Vector3(5, 5, 0), Vector3.one * 2);
            List<Bounds> bounds = new()
            {
                target,
                new(new Vector3(15, 15, 0), Vector3.one * 2),
                new(new Vector3(25, 25, 0), Vector3.one * 2),
            };
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);

            Bounds searchBounds = new(new Vector3(5, 5, 0), Vector3.one * 1);
            List<Bounds> results = QueryBounds(tree, searchBounds);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(target, results[0]);
        }

        [Test]
        public void GetElementsInRangeOnEmptyBoundsTreeReturnsEmpty()
        {
            List<Bounds> bounds = new();
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);

            List<Bounds> results = QueryRange(tree, Vector2.zero, 100f);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetElementsInRangeReturnsElementsInCircle()
        {
            List<Bounds> bounds = new();
            for (int x = 0; x < 10; x++)
            {
                for (int y = 0; y < 10; y++)
                {
                    bounds.Add(new Bounds(new Vector3(x * 10, y * 10, 0), Vector3.one * 2));
                }
            }
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);

            Vector2 center = new(50, 50);
            float range = 30f;
            List<Bounds> results = QueryRange(tree, center, range);

            Assert.Greater(results.Count, 0);

            foreach (Bounds result in results)
            {
                float distance = Vector2.Distance(
                    center,
                    new Vector2(result.center.x, result.center.y)
                );

                Assert.Less(distance, range * 2f);
            }
        }

        [Test]
        public void GetElementsInRangeWithMinimumRangeExcludesNearElements()
        {
            Vector2 center = Vector2.zero;
            List<Bounds> bounds = new()
            {
                new(new Vector3(2, 0, 0), Vector3.one),
                new(new Vector3(10, 0, 0), Vector3.one),
                new(new Vector3(20, 0, 0), Vector3.one),
            };
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);

            List<Bounds> results = QueryRange(tree, center, 15f, minimumRange: 5f);
            Assert.AreEqual(1, results.Count);
        }

        [Test]
        public void GetApproximateNearestNeighborsWithEmptyTreeReturnsEmpty()
        {
            List<Bounds> bounds = new();
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);
            List<Bounds> results = new();

            tree.GetApproximateNearestNeighbors(Vector2.zero, 5, results);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetApproximateNearestNeighborsWithCountZeroReturnsEmpty()
        {
            List<Bounds> bounds = new()
            {
                new(Vector3.zero, Vector3.one),
                new(Vector3.one * 10, Vector3.one),
            };
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);
            List<Bounds> results = new();

            tree.GetApproximateNearestNeighbors(Vector2.zero, 0, results);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetApproximateNearestNeighborsReturnsRequestedCount()
        {
            List<Bounds> bounds = new();
            for (int i = 0; i < 50; i++)
            {
                bounds.Add(
                    new Bounds(
                        new Vector3(Random.NextFloat(-100, 100), Random.NextFloat(-100, 100), 0),
                        Vector3.one * 2
                    )
                );
            }
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);
            List<Bounds> results = new();

            int requestedCount = 10;
            tree.GetApproximateNearestNeighbors(Vector2.zero, requestedCount, results);
            Assert.AreEqual(requestedCount, results.Count);
        }

        [Test]
        public void GetApproximateNearestNeighborsWithCountGreaterThanElementsReturnsAll()
        {
            List<Bounds> bounds = new()
            {
                new(Vector3.zero, Vector3.one),
                new(Vector3.one * 10, Vector3.one),
                new(Vector3.right * 10, Vector3.one),
            };
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);
            List<Bounds> results = new();

            tree.GetApproximateNearestNeighbors(Vector2.zero, 100, results);
            Assert.AreEqual(3, results.Count);
        }

        [Test]
        public void GetApproximateNearestNeighborsReturnsClosestElements()
        {
            Vector2 center = Vector2.zero;
            List<Bounds> bounds = new()
            {
                new(new Vector3(5, 0, 0), Vector3.one),
                new(new Vector3(0, 5, 0), Vector3.one),
                new(new Vector3(500, 500, 0), Vector3.one),
                new(new Vector3(1000, 1000, 0), Vector3.one),
            };
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);
            List<Bounds> results = new();

            tree.GetApproximateNearestNeighbors(center, 2, results);
            Assert.AreEqual(2, results.Count);

            foreach (Bounds result in results)
            {
                float distance = Vector2.Distance(
                    center,
                    new Vector2(result.center.x, result.center.y)
                );
                Assert.Less(distance, 50f);
            }
        }

        [Test]
        public void BoundaryCalculatedCorrectlyForPositiveBounds()
        {
            List<Bounds> bounds = new()
            {
                new(new Vector3(0, 0, 0), Vector3.one * 2),
                new(new Vector3(20, 0, 0), Vector3.one * 2),
                new(new Vector3(0, 20, 0), Vector3.one * 2),
                new(new Vector3(20, 20, 0), Vector3.one * 2),
            };
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);

            Bounds boundary = tree.Boundary;
            Assert.Greater(boundary.size.x, 0);
            Assert.Greater(boundary.size.y, 0);
        }

        [Test]
        public void BoundaryCalculatedCorrectlyForNegativeBounds()
        {
            List<Bounds> bounds = new()
            {
                new(new Vector3(-20, -20, 0), Vector3.one * 2),
                new(new Vector3(-10, -10, 0), Vector3.one * 2),
                new(new Vector3(-5, -5, 0), Vector3.one * 2),
            };
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);

            Bounds boundary = tree.Boundary;
            Assert.IsTrue(boundary.Contains(new Vector3(-10, -10, 0)));
        }

        [Test]
        public void LargeDatasetStressTest()
        {
            List<Bounds> bounds = new();
            for (int i = 0; i < 10000; i++)
            {
                bounds.Add(
                    new Bounds(
                        new Vector3(
                            Random.NextFloat(-1000, 1000),
                            Random.NextFloat(-1000, 1000),
                            0
                        ),
                        Vector3.one * Random.NextFloat(1, 10)
                    )
                );
            }
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);

            List<Bounds> allResults = QueryBounds(
                tree,
                new Bounds(Vector3.zero, Vector3.one * 100000)
            );
            Assert.AreEqual(10000, allResults.Count);

            List<Bounds> results = QueryRange(tree, Vector2.zero, 50f);
            Assert.GreaterOrEqual(results.Count, 0);
        }

        [Test]
        public void CustomBucketSizeAffectsTreeStructure()
        {
            List<Bounds> bounds = new();
            for (int i = 0; i < 100; i++)
            {
                bounds.Add(new Bounds(new Vector3(i * 10, i * 10, 0), Vector3.one * 2));
            }

            RTree2D<Bounds> treeSmallBucket = new(bounds, b => b, bucketSize: 1);
            RTree2D<Bounds> treeLargeBucket = new(bounds, b => b, bucketSize: 100);

            List<Bounds> resultsSmall = QueryBounds(
                treeSmallBucket,
                new Bounds(Vector3.zero, Vector3.one * 100000)
            );
            List<Bounds> resultsLarge = QueryBounds(
                treeLargeBucket,
                new Bounds(Vector3.zero, Vector3.one * 100000)
            );

            Assert.AreEqual(100, resultsSmall.Count);
            Assert.AreEqual(100, resultsLarge.Count);
        }

        [Test]
        public void CustomBranchFactorAffectsTreeStructure()
        {
            List<Bounds> bounds = new();
            for (int i = 0; i < 100; i++)
            {
                bounds.Add(new Bounds(new Vector3(i * 10, i * 10, 0), Vector3.one * 2));
            }

            RTree2D<Bounds> treeSmallBranch = new(bounds, b => b, branchFactor: 2);
            RTree2D<Bounds> treeLargeBranch = new(bounds, b => b, branchFactor: 16);

            List<Bounds> resultsSmall = QueryBounds(
                treeSmallBranch,
                new Bounds(Vector3.zero, Vector3.one * 100000)
            );
            List<Bounds> resultsLarge = QueryBounds(
                treeLargeBranch,
                new Bounds(Vector3.zero, Vector3.one * 100000)
            );

            Assert.AreEqual(100, resultsSmall.Count);
            Assert.AreEqual(100, resultsLarge.Count);
        }

        [Test]
        public void OverlappingBoundsHandledCorrectly()
        {
            List<Bounds> bounds = new()
            {
                new(new Vector3(0, 0, 0), Vector3.one * 20),
                new(new Vector3(5, 5, 0), Vector3.one * 10),
                new(new Vector3(10, 10, 0), Vector3.one * 5),
            };
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);

            Bounds searchBounds = new(new Vector3(7, 7, 0), Vector3.one * 2);
            List<Bounds> results = QueryBounds(tree, searchBounds);

            Assert.AreEqual(3, results.Count);
        }

        [Test]
        public void AdjacentBoundsHandledCorrectly()
        {
            List<Bounds> bounds = new()
            {
                new(new Vector3(0, 0, 0), Vector3.one * 10),
                new(new Vector3(10, 0, 0), Vector3.one * 10),
                new(new Vector3(20, 0, 0), Vector3.one * 10),
            };
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);

            Bounds searchBounds = new(new Vector3(10, 0, 0), Vector3.one * 10);
            List<Bounds> results = QueryBounds(tree, searchBounds);

            Assert.GreaterOrEqual(results.Count, 1);
        }

        [Test]
        public void VerySmallBoundsAreDistinguished()
        {
            List<Bounds> bounds = new()
            {
                new(new Vector3(0, 0, 0), Vector3.one * 0.001f),
                new(new Vector3(0.01f, 0, 0), Vector3.one * 0.001f),
                new(new Vector3(0, 0.01f, 0), Vector3.one * 0.001f),
            };
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);

            List<Bounds> results = QueryBounds(tree, new Bounds(Vector3.zero, Vector3.one * 0.1f));
            Assert.AreEqual(3, results.Count);
        }

        [Test]
        public void VeryLargeBoundsHandledCorrectly()
        {
            List<Bounds> bounds = new()
            {
                new(Vector3.zero, Vector3.one * 10000),
                new(new Vector3(5000, 5000, 0), Vector3.one * 5000),
                new(new Vector3(100, 100, 0), Vector3.one * 10),
            };
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);

            List<Bounds> results = QueryBounds(
                tree,
                new Bounds(new Vector3(50, 50, 0), Vector3.one * 20)
            );

            Assert.Greater(results.Count, 0);
        }

        [Test]
        public void MultipleQueriesOnSameTreeReturnConsistentResults()
        {
            List<Bounds> bounds = new();
            for (int i = 0; i < 50; i++)
            {
                bounds.Add(
                    new Bounds(
                        new Vector3(Random.NextFloat(-50, 50), Random.NextFloat(-50, 50), 0),
                        Vector3.one * 2
                    )
                );
            }
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);

            Bounds queryBounds = new(Vector3.zero, Vector3.one * 30);

            List<Bounds> results1 = QueryBounds(tree, queryBounds);
            List<Bounds> results2 = QueryBounds(tree, queryBounds);

            Assert.AreEqual(results1.Count, results2.Count);
            CollectionAssert.AreEquivalent(results1, results2);
        }

        [Test]
        public void LinearArrangementOfBoundsHandledCorrectly()
        {
            List<Bounds> bounds = new();
            for (int i = 0; i < 100; i++)
            {
                bounds.Add(new Bounds(new Vector3(i * 10, 0, 0), Vector3.one * 2));
            }
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);

            List<Bounds> allResults = QueryBounds(
                tree,
                new Bounds(Vector3.zero, new Vector3(100000, 100, 1))
            );
            Assert.AreEqual(100, allResults.Count);

            List<Bounds> results = QueryBounds(
                tree,
                new Bounds(new Vector3(500, 0, 0), Vector3.one * 50)
            );
            Assert.Greater(results.Count, 0);
        }

        [Test]
        public void GridPatternOfBoundsHandledCorrectly()
        {
            List<Bounds> bounds = new();
            for (int x = 0; x < 10; x++)
            {
                for (int y = 0; y < 10; y++)
                {
                    bounds.Add(new Bounds(new Vector3(x * 10, y * 10, 0), Vector3.one * 4));
                }
            }
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);

            Bounds searchBounds = new(new Vector3(50, 50, 0), Vector3.one * 20);
            List<Bounds> results = QueryBounds(tree, searchBounds);

            Assert.Greater(results.Count, 0);
        }

        [Test]
        public void GetElementsInBoundsReusesProvidedList()
        {
            List<Bounds> bounds = new();
            for (int i = 0; i < 1000; i++)
            {
                bounds.Add(new Bounds(new Vector3(i, 0, 0), Vector3.one));
            }
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);

            List<Bounds> buffer = new();
            List<Bounds> results = tree.GetElementsInBounds(
                new Bounds(Vector3.zero, Vector3.one * 100000),
                buffer
            );

            Assert.AreSame(buffer, results);
            Assert.Greater(results.Count, 0);
        }

        [Test]
        public void ZeroSizeBoundsHandledCorrectly()
        {
            List<Bounds> bounds = new()
            {
                new(new Vector3(5, 5, 0), Vector3.zero),
                new(new Vector3(10, 10, 0), Vector3.zero),
                new(new Vector3(15, 15, 0), Vector3.zero),
            };
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);

            List<Bounds> results = QueryBounds(
                tree,
                new Bounds(new Vector3(10, 10, 0), Vector3.one * 5)
            );
            Assert.Greater(results.Count, 0);
        }

        [Test]
        public void NegativeSizeBoundsHandledGracefully()
        {
            List<Bounds> bounds = new()
            {
                new(new Vector3(5, 5, 0), new Vector3(-2, -2, -1)),
                new(new Vector3(10, 10, 0), Vector3.one * 2),
            };

            RTree2D<Bounds> tree = CreateBoundsTree(bounds);
            Assert.IsTrue(tree != null);
        }

        [Test]
        public void BoundsAtTreeEdgeHandledCorrectly()
        {
            List<Bounds> bounds = new()
            {
                new(new Vector3(-100, -100, 0), Vector3.one * 2),
                new(new Vector3(100, 100, 0), Vector3.one * 2),
                new(new Vector3(-100, 100, 0), Vector3.one * 2),
                new(new Vector3(100, -100, 0), Vector3.one * 2),
            };
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);

            List<Bounds> results = QueryRange(tree, Vector2.zero, 200f);
            Assert.AreEqual(4, results.Count);
        }

        [Test]
        public void StrPackingAlgorithmCreatesBalancedStructure()
        {
            List<Bounds> bounds = new();
            for (int i = 0; i < 100; i++)
            {
                bounds.Add(new Bounds(new Vector3(i, i, 0), Vector3.one));
            }

            RTree2D<Bounds> tree = CreateBoundsTree(bounds);

            List<Bounds> corner1 = QueryBounds(
                tree,
                new Bounds(new Vector3(10, 10, 0), Vector3.one * 10)
            );
            List<Bounds> corner2 = QueryBounds(
                tree,
                new Bounds(new Vector3(80, 80, 0), Vector3.one * 10)
            );

            Assert.Greater(corner1.Count, 0);
            Assert.Greater(corner2.Count, 0);
        }

        [Test]
        public void ColinearPointsHandledCorrectly()
        {
            List<Bounds> bounds = new();
            for (int i = 0; i < 100; i++)
            {
                bounds.Add(new Bounds(new Vector3(i, 0, 0), Vector3.one * 0.5f));
            }
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);

            Assert.IsTrue(tree != null);

            Assert.AreEqual(100, tree.elements.Length);

            List<Bounds> results = QueryRange(tree, new Vector2(50, 0), 10f);
            Assert.Greater(results.Count, 0, "Should find bounds within range of (50, 0)");

            foreach (Bounds result in results)
            {
                float distance = Vector2.Distance(result.center, new Vector2(50, 0));
                Assert.LessOrEqual(
                    distance,
                    10.5f,
                    $"Bound center {result.center} should be within range 10 + bound radius"
                );
                Assert.AreEqual(0, result.center.y, 0.01f, "All bounds should be on x-axis (y=0)");
            }

            results = QueryRange(tree, new Vector2(0, 0), 5f);
            Assert.Greater(results.Count, 0, "Should find bounds at start of line");

            results = QueryRange(tree, new Vector2(99, 0), 5f);
            Assert.Greater(results.Count, 0, "Should find bounds at end of line");

            results = QueryRange(tree, new Vector2(50, 100), 5f);
            Assert.AreEqual(0, results.Count, "Should find no bounds far from the line");

            results = QueryBounds(tree, new Bounds(new Vector3(50, 0, 0), new Vector3(20, 5, 1)));
            Assert.Greater(
                results.Count,
                0,
                "Should find bounds in search bounds centered at (50, 0)"
            );
            foreach (Bounds result in results)
            {
                Assert.AreEqual(0, result.center.y, 0.01f, "All bounds should be on x-axis (y=0)");
            }
        }

        [Test]
        public void SingleLineVerticalPointsHandledCorrectly()
        {
            List<Bounds> bounds = new();
            for (int i = 0; i < 100; i++)
            {
                bounds.Add(new Bounds(new Vector3(0, i, 0), Vector3.one * 0.5f));
            }
            RTree2D<Bounds> tree = CreateBoundsTree(bounds);

            Assert.IsTrue(tree != null);

            Assert.AreEqual(100, tree.elements.Length);

            List<Bounds> results = QueryRange(tree, new Vector2(0, 50), 10f);
            Assert.Greater(results.Count, 0, "Should find bounds within range of (0, 50)");

            foreach (Bounds result in results)
            {
                float distance = Vector2.Distance(result.center, new Vector2(0, 50));
                Assert.LessOrEqual(
                    distance,
                    10.5f,
                    $"Bound center {result.center} should be within range 10 + bound radius"
                );
                Assert.AreEqual(0, result.center.x, 0.01f, "All bounds should be on y-axis (x=0)");
            }

            results = QueryRange(tree, new Vector2(0, 0), 5f);
            Assert.Greater(results.Count, 0, "Should find bounds at start of line");

            results = QueryRange(tree, new Vector2(0, 99), 5f);
            Assert.Greater(results.Count, 0, "Should find bounds at end of line");

            results = QueryRange(tree, new Vector2(100, 50), 5f);
            Assert.AreEqual(0, results.Count, "Should find no bounds far from the line");

            results = QueryBounds(tree, new Bounds(new Vector3(0, 50, 0), new Vector3(5, 20, 1)));
            Assert.Greater(
                results.Count,
                0,
                "Should find bounds in search bounds centered at (0, 50)"
            );
            foreach (Bounds result in results)
            {
                Assert.AreEqual(0, result.center.x, 0.01f, "All bounds should be on y-axis (x=0)");
                Assert.GreaterOrEqual(
                    result.center.y,
                    39.5f,
                    "Bounds should be near or within y range"
                );
                Assert.LessOrEqual(
                    result.center.y,
                    60.5f,
                    "Bounds should be near or within y range"
                );
            }
        }

        [Test]
        public void IdenticalBoundsQueriesHandled()
        {
            const int count = 48;
            Bounds repeated = new(new Vector3(12f, -4f, 0f), Vector3.zero);
            List<Bounds> bounds = new();
            for (int i = 0; i < count; ++i)
            {
                bounds.Add(repeated);
            }

            RTree2D<Bounds> tree = CreateBoundsTree(bounds);

            List<Bounds> rangeResults = QueryRange(
                tree,
                new Vector2(repeated.center.x, repeated.center.y),
                1f
            );
            Assert.AreEqual(count, rangeResults.Count);
            foreach (Bounds result in rangeResults)
            {
                Assert.AreEqual(repeated.center, result.center);
                Assert.AreEqual(repeated.size, result.size);
            }

            List<Bounds> boundsResults = QueryBounds(
                tree,
                new Bounds(repeated.center, new Vector3(2f, 2f, 1f))
            );
            Assert.AreEqual(count, boundsResults.Count);
            foreach (Bounds result in boundsResults)
            {
                Assert.AreEqual(repeated.center, result.center);
                Assert.AreEqual(repeated.size, result.size);
            }

            // Equal values remain distinct entries; value-based deduplication would discard valid neighbors.
            List<Bounds> neighbors = new();
            tree.GetApproximateNearestNeighbors(
                new Vector2(repeated.center.x, repeated.center.y),
                count * 2,
                neighbors
            );
            Assert.AreEqual(count, neighbors.Count);
            foreach (Bounds neighbor in neighbors)
            {
                Assert.AreEqual(repeated.center, neighbor.center);
                Assert.AreEqual(repeated.size, neighbor.size);
            }
        }

        /// <summary>
        /// Zero range means "the query point touches the element's box", so an element 0.0005 units
        /// away is out. The circle predicate this used to run through ends
        /// <c>distanceSquared &lt;= radiusSquared + 1e-6f</c>, and at radius zero that constant
        /// <i>is</i> the predicate: it admits anything within 1e-3 world units, and nothing at all
        /// once the coordinates are large enough that 1e-3 is below one ULP.
        /// </summary>
        [Test]
        public void GetElementsInRangeWithZeroRangeMeansTouches()
        {
            List<Vector2> points = new()
            {
                Vector2.zero,
                new Vector2(0.0005f, 0f),
                new Vector2(0.002f, 0f),
            };

            RTree2D<Vector2> tree = new(points, ZeroSizeBounds);
            List<Vector2> results = new();

            tree.GetElementsInRange(Vector2.zero, 0f, results);

            CollectionAssert.AreEquivalent(new[] { Vector2.zero }, results);
        }

        /// <summary>
        /// The same epsilon, made visible at a radius large enough for the element to survive the
        /// candidate box: a point whose squared distance is <c>range * range + 5e-7</c> is outside
        /// the circle, and an implementation that widened the circle by <c>1e-6</c> would return it.
        /// </summary>
        [Test]
        public void GetElementsInRangeDoesNotWidenTheCircleByAnEpsilon()
        {
            const float range = 1f;
            Vector2 justOutside = new(0.6f, 0.80000031f);
            Assert.Less(
                range * range,
                justOutside.sqrMagnitude,
                "The fixture point is inside the circle, so it proves nothing."
            );
            Assert.Less(
                justOutside.sqrMagnitude,
                (range * range) + 1e-6f,
                "The fixture point is outside the old epsilon, so it proves nothing."
            );

            List<Vector2> points = new() { justOutside, new Vector2(0.5f, 0.5f) };
            RTree2D<Vector2> tree = new(points, ZeroSizeBounds);
            List<Vector2> results = new();

            tree.GetElementsInRange(Vector2.zero, range, results);

            CollectionAssert.AreEquivalent(new[] { new Vector2(0.5f, 0.5f) }, results);
        }

        private static Bounds ZeroSizeBounds(Vector2 point)
        {
            return new Bounds(new Vector3(point.x, point.y, 0f), Vector3.zero);
        }
    }
}
