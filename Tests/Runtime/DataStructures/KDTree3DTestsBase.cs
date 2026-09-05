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
    using Vector3 = UnityEngine.Vector3;

    public abstract class KDTree3DTestsBase : SpatialTree3DTests<KdTree3D<Vector3>>
    {
        // Reseed each test so a failing tree can be reproduced alone or within the fixture.
        private const uint RandomSeed = 0x5EED0304;

        private IRandom _random = new PcgRandom(RandomSeed);

        private IRandom Random => _random;

        [SetUp]
        public void SeedKdTree3DRandom()
        {
            _random = new PcgRandom(RandomSeed);
        }

        protected abstract bool IsBalanced { get; }

        protected override KdTree3D<Vector3> CreateTree(IEnumerable<Vector3> points)
        {
            return new KdTree3D<Vector3>(points, point => point, balanced: IsBalanced);
        }

        [Test]
        public void ConstructorWithNullPointsThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                new KdTree3D<Vector3>(null, point => point, balanced: IsBalanced);
            });
        }

        [Test]
        public void ConstructorWithNullTransformerThrowsArgumentNullException()
        {
            List<Vector3> points = new() { Vector3.zero };
            Assert.Throws<ArgumentNullException>(() =>
            {
                new KdTree3D<Vector3>(points, null, balanced: IsBalanced);
            });
        }

        [Test]
        public void ConstructorWithEmptyCollectionSucceeds()
        {
            List<Vector3> points = new();
            KdTree3D<Vector3> tree = CreateTree(points);
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
            KdTree3D<Vector3> tree = CreateTree(points);

            List<Vector3> results = new();
            tree.GetElementsInRange(point, 1f, results);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(point, results[0]);
        }

        [Test]
        public void ConstructorWithDuplicateElementsPreservesAll()
        {
            Vector3 point = new(5f, -3f, 2f);
            List<Vector3> points = new() { point, point, point };
            KdTree3D<Vector3> tree = CreateTree(points);

            List<Vector3> results = new();
            tree.GetElementsInRange(point, 0.1f, results);
            Assert.AreEqual(3, results.Count);
            Assert.IsTrue(results.All(candidate => candidate == point));
        }

        [Test]
        public void GetElementsInRangeWithEmptyTreeReturnsEmptyAdditional()
        {
            KdTree3D<Vector3> tree = CreateTree(new List<Vector3>());
            List<Vector3> results = new();

            tree.GetElementsInRange(Vector3.zero, 10f, results);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetElementsInRangeWithZeroRangeReturnsOnlyExactMatchesAdditional()
        {
            Vector3 target = new(10f, -4f, 2f);
            List<Vector3> points = new()
            {
                target,
                target,
                target,
                target + new Vector3(0.01f, 0f, 0f),
                target + new Vector3(0f, 0.01f, 0f),
            };

            KdTree3D<Vector3> tree = CreateTree(points);

            List<Vector3> results = new() { new Vector3(999f, 999f, 999f) };
            tree.GetElementsInRange(target, 0f, results);

            Assert.AreEqual(3, results.Count);
            Assert.IsTrue(results.All(candidate => candidate == target));
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

            KdTree3D<Vector3> tree = CreateTree(points);
            List<Vector3> results = new();
            tree.GetElementsInRange(Vector3.zero, 5.5f, results, minimumRange: 2f);

            Vector3[] expected = { points[1], points[2] };
            CollectionAssert.AreEquivalent(expected, results);
        }

        [Test]
        public void MultipleQueriesOnSameTreeReturnConsistentResults()
        {
            List<Vector3> points = new();
            for (int i = 0; i < 64; ++i)
            {
                points.Add(
                    new Vector3(
                        Random.NextFloat(-25, 25),
                        Random.NextFloat(-25, 25),
                        Random.NextFloat(-25, 25)
                    )
                );
            }

            KdTree3D<Vector3> tree = CreateTree(points);

            List<Vector3> first = new();
            List<Vector3> second = new();
            Vector3 query = Vector3.zero;
            float range = 15f;

            tree.GetElementsInRange(query, range, first);
            tree.GetElementsInRange(query, range, second);

            Assert.AreEqual(first.Count, second.Count);
            CollectionAssert.AreEquivalent(first, second);
        }

        [Test]
        public void WorstCaseSortedInputHandledCorrectly()
        {
            List<Vector3> points = new();
            for (int i = 0; i < 1_000; ++i)
            {
                points.Add(new Vector3(i, 0f, 0f));
            }

            KdTree3D<Vector3> tree = CreateTree(points);
            List<Vector3> results = new();

            tree.GetElementsInRange(new Vector3(500f, 0f, 0f), 25f, results);
            Assert.Greater(results.Count, 0);
        }

        [Test]
        public void AlternatingPatternHandledCorrectly()
        {
            List<Vector3> points = new();
            for (int i = 0; i < 100; ++i)
            {
                if (i % 2 == 0)
                {
                    points.Add(new Vector3(i, 0f, 0f));
                }
                else
                {
                    points.Add(new Vector3(0f, i, 0f));
                }
            }

            KdTree3D<Vector3> tree = CreateTree(points);
            List<Vector3> results = new();

            tree.GetElementsInRange(Vector3.zero, 30f, results);
            Assert.Greater(results.Count, 0);
        }

        [Test]
        public void BalancedVsUnbalancedBothFindSameElements()
        {
            List<Vector3> points = new();
            for (int i = 0; i < 128; ++i)
            {
                points.Add(
                    new Vector3(
                        Random.NextFloat(-50, 50),
                        Random.NextFloat(-50, 50),
                        Random.NextFloat(-50, 50)
                    )
                );
            }

            KdTree3D<Vector3> balancedTree = new(points, point => point, balanced: true);
            KdTree3D<Vector3> unbalancedTree = new(points, point => point, balanced: false);

            Vector3 query = Vector3.zero;
            float range = 40f;

            List<Vector3> balancedResults = new();
            List<Vector3> unbalancedResults = new();

            balancedTree.GetElementsInRange(query, range, balancedResults);
            unbalancedTree.GetElementsInRange(query, range, unbalancedResults);

            Assert.AreEqual(balancedResults.Count, unbalancedResults.Count);
            CollectionAssert.AreEquivalent(balancedResults, unbalancedResults);
        }

        [Test]
        public void GetElementsInBoundsIncludesUpperBoundaryPoint()
        {
            List<Vector3> points = new() { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f) };
            KdTree3D<Vector3> tree = CreateTree(points);
            Bounds bounds = new(new Vector3(0.5f, 0f, 0f), new Vector3(1f, 0.1f, 0.1f));
            List<Vector3> results = new();
            tree.GetElementsInBounds(bounds, results);
            CollectionAssert.AreEquivalent(points, results);
        }

        [Test]
        public void EdgeAlignedBoundsOnGridIncludesMaxIndexPoints()
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

            KdTree3D<Vector3> tree = CreateTree(points);
            Bounds bounds = new(new Vector3(4.5f, 4.5f, 4.5f), new Vector3(9f, 9f, 9f));
            List<Vector3> results = new();
            tree.GetElementsInBounds(bounds, results);
            Assert.AreEqual(1000, results.Count);
        }

        [Test]
        [Timeout(15000)]
        public void FullBoundsOnHundredGridReturnsAllElements()
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

            KdTree3D<Vector3> tree = CreateTree(points);
            Bounds bounds = new(new Vector3(49.5f, 49.5f, 49.5f), new Vector3(99f, 99f, 99f));

            List<Vector3> results = new();
            tree.GetElementsInBounds(bounds, results);

            Assert.AreEqual(
                1_000_000,
                results.Count,
                "Expected full dataset bounds to return all elements, but received {0}.",
                results.Count
            );
        }
    }
}
