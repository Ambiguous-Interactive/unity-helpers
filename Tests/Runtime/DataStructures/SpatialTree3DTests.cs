// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Random;
    using Bounds = UnityEngine.Bounds;
    using Vector3 = UnityEngine.Vector3;

    public abstract class SpatialTree3DTests<TTree>
        where TTree : ISpatialTree3D<Vector3>
    {
        private IRandom Random => PRNG.Instance;

        protected abstract TTree CreateTree(IEnumerable<Vector3> points);

        [Test]
        public void SimpleWithinSphere()
        {
            Vector3 center = new(
                Random.NextFloat(-100, 100),
                Random.NextFloat(-100, 100),
                Random.NextFloat(-100, 100)
            );

            float radius = Random.NextFloat(5, 25f);

            const int numPoints = 1_000;

            HashSet<Vector3> points = new(numPoints);

            for (int i = 0; i < numPoints; ++i)
            {
                Vector3 point;

                do
                {
                    point = GetRandomPointInSphere(center, radius);
                } while (!points.Add(point));
            }

            TTree tree = CreateTree(points);

            List<Vector3> pointsInRange = new();

            tree.GetElementsInRange(center, radius, pointsInRange);

            Assert.IsTrue(
                points.SetEquals(pointsInRange),
                "Found {0} points in range, expected {1}.",
                pointsInRange.Count,
                points.Count
            );

            Vector3 offset = center;

            offset.x -= radius * 2;

            offset.y -= radius * 2;

            offset.z -= radius * 2;

            tree.GetElementsInRange(offset, radius, pointsInRange);

            Assert.AreEqual(
                0,
                pointsInRange.Count,
                "Found {0} points within {1} range of {2} (original center {3})",
                pointsInRange.Count,
                radius,
                offset,
                center
            );
        }

        [Test]
        public void SimplePointOutsideRange()
        {
            Vector3 point = new(
                Random.NextFloat(-100, 100),
                Random.NextFloat(-100, 100),
                Random.NextFloat(-100, 100)
            );

            Vector3 direction = GetRandomPointInSphere(Vector3.zero, 1f).normalized;

            float range = Random.NextFloat(25, 1_000);

            Vector3 testPoint = point + (direction * range);

            List<Vector3> points = new(1) { testPoint };

            TTree tree = CreateTree(points);

            List<Vector3> pointsInRange = new();

            tree.GetElementsInRange(point, range * 0.99f, pointsInRange).ToList();

            Assert.AreEqual(0, pointsInRange.Count);

            tree.GetElementsInRange(point, range * 1.01f, pointsInRange);

            Assert.AreEqual(
                1,
                pointsInRange.Count,
                "Failed to find point {0} from test point {1} with {2:0.00} range.",
                point,
                testPoint,
                range
            );

            Assert.AreEqual(testPoint, pointsInRange[0]);
        }

        [Test]
        public void SimpleAnn()
        {
            List<Vector3> points = new();

            for (int x = 0; x < 50; ++x)
            {
                for (int y = 0; y < 50; ++y)
                {
                    for (int z = 0; z < 50; ++z)
                    {
                        Vector3 point = new(x, y, z);

                        points.Add(point);
                    }
                }
            }

            TTree tree = CreateTree(points);

            Vector3 center = tree.Boundary.center;

            List<Vector3> nearestNeighbors = new();

            int nearestNeighborCount = 1;

            tree.GetApproximateNearestNeighbors(center, nearestNeighborCount, nearestNeighbors);

            Assert.AreEqual(nearestNeighborCount, nearestNeighbors.Count);

            Assert.IsTrue(nearestNeighbors.All(neighbor => (neighbor - center).magnitude <= 2f));

            nearestNeighborCount = 8;

            tree.GetApproximateNearestNeighbors(center, nearestNeighborCount, nearestNeighbors);

            Assert.AreEqual(nearestNeighborCount, nearestNeighbors.Count);

            Assert.IsTrue(nearestNeighbors.All(neighbor => (neighbor - center).magnitude <= 3f));

            nearestNeighborCount = 27;

            tree.GetApproximateNearestNeighbors(center, nearestNeighborCount, nearestNeighbors);

            Assert.AreEqual(nearestNeighborCount, nearestNeighbors.Count);

            Assert.IsTrue(
                nearestNeighbors.All(neighbor => (neighbor - center).magnitude <= 6f),
                "Max: {0}",
                nearestNeighbors.Select(neighbor => (neighbor - center).magnitude).Max()
            );

            center = new Vector3(-100, -100, -100);

            tree.GetApproximateNearestNeighbors(center, nearestNeighborCount, nearestNeighbors);

            Assert.AreEqual(nearestNeighborCount, nearestNeighbors.Count);
        }

        [Test]
        public void GetElementsInRangeWithVeryLargeRangeReturnsAllPoints()
        {
            List<Vector3> points = new();

            for (int i = 0; i < 64; ++i)
            {
                points.Add(
                    new Vector3(
                        Random.NextFloat(-50, 50),
                        Random.NextFloat(-50, 50),
                        Random.NextFloat(-50, 50)
                    )
                );
            }

            TTree tree = CreateTree(points);

            Vector3 queryPoint = Vector3.zero;

            float maxDistance = points
                .Select(point => (point - queryPoint).magnitude)
                .DefaultIfEmpty(0f)
                .Max();

            List<Vector3> results = new() { Vector3.one };

            tree.GetElementsInRange(queryPoint, maxDistance + 10f, results);

            CollectionAssert.AreEquivalent(points, results);
        }

        [Test]
        public void GetElementsInRangeRespectsMinimumRange()
        {
            List<Vector3> points = new()
            {
                new Vector3(0f, 0f, 1f),
                new Vector3(0f, 0f, 3f),
                new Vector3(0f, 0f, 5f),
                new Vector3(0f, 0f, 7f),
            };

            TTree tree = CreateTree(points);

            List<Vector3> results = new();

            tree.GetElementsInRange(Vector3.zero, 5.5f, results, minimumRange: 2f);

            Vector3[] expected = { points[1], points[2] };

            CollectionAssert.AreEquivalent(expected, results);
        }

        [Test]
        public void GetElementsInRangeClearsResultsList()
        {
            List<Vector3> points = new() { new Vector3(0f, 0f, 0f), new Vector3(1f, 1f, 1f) };

            TTree tree = CreateTree(points);

            Vector3 sentinel = new(999f, 999f, 999f);

            List<Vector3> results = new() { sentinel };

            tree.GetElementsInRange(Vector3.zero, 10f, results);

            Assert.IsFalse(results.Contains(sentinel));

            CollectionAssert.AreEquivalent(points, results);
        }

        [Test]
        public void GetElementsInBoundsReturnsPointsInside()
        {
            List<Vector3> points = new()
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(2f, 2f, 2f),
                new Vector3(4f, 4f, 4f),
                new Vector3(10f, 10f, 10f),
            };

            TTree tree = CreateTree(points);

            Bounds bounds = new(new Vector3(2f, 2f, 2f), new Vector3(8f, 8f, 8f));

            List<Vector3> results = new();

            tree.GetElementsInBounds(bounds, results);

            BoundingBox3D queryBounds = BoundingBox3D.FromClosedBounds(bounds);

            List<Vector3> expected = points.Where(queryBounds.Contains).ToList();

            CollectionAssert.AreEquivalent(expected, results);
        }

        [Test]
        public void GetElementsInBoundsClearsResultsList()
        {
            List<Vector3> points = new() { new Vector3(-1f, 0f, 0f), new Vector3(1f, 0f, 0f) };

            TTree tree = CreateTree(points);

            Vector3 sentinel = new(-999f, -999f, -999f);

            List<Vector3> results = new() { sentinel };

            tree.GetElementsInBounds(new Bounds(Vector3.zero, Vector3.one * 10f), results);

            Assert.IsFalse(results.Contains(sentinel));

            CollectionAssert.AreEquivalent(points, results);
        }

        [Test]
        public void GetApproximateNearestNeighborsReturnsAllWhenRequestExceedsCount()
        {
            List<Vector3> points = new()
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 1f, 0f),
            };

            TTree tree = CreateTree(points);

            List<Vector3> results = new();

            tree.GetApproximateNearestNeighbors(Vector3.zero, 10, results);

            CollectionAssert.AreEquivalent(points, results);
        }

        private Vector3 GetRandomPointInSphere(Vector3 center, float radius)
        {
            float radiusSquared = radius * radius;

            while (true)
            {
                Vector3 offset = new(
                    Random.NextFloat(-radius, radius),
                    Random.NextFloat(-radius, radius),
                    Random.NextFloat(-radius, radius)
                );

                if (radiusSquared < offset.sqrMagnitude)
                {
                    continue;
                }

                Vector3 point = center + offset;

                // Validating the offset is not the same as validating the point: `center + offset`
                // rounds, so the offset a tree recovers as `point - center` can be a couple of ULPs
                // longer than the one accepted above -- enough to put the point outside the query
                // sphere, which every tree is then right to exclude. Re-test with the trees' own
                // predicate so the expected set only contains points that are genuinely in range.
                if (radiusSquared < (point - center).sqrMagnitude)
                {
                    continue;
                }

                return point;
            }
        }

        [Test]
        public void GetElementsInRangeWithEmptyTreeReturnsEmpty()
        {
            TTree tree = CreateTree(Enumerable.Empty<Vector3>());

            List<Vector3> results = new() { new Vector3(123f, 456f, 789f) };

            tree.GetElementsInRange(Vector3.zero, 10f, results);

            Assert.IsEmpty(results);
        }

        [Test]
        public void GetElementsInRangeWithNegativeRangeReturnsEmpty()
        {
            List<Vector3> points = new() { new Vector3(1f, 1f, 1f) };

            TTree tree = CreateTree(points);

            List<Vector3> results = new() { new Vector3(123f, 456f, 789f) };

            tree.GetElementsInRange(points[0], -1f, results);

            Assert.IsEmpty(results);
        }

        [Test]
        public void GetElementsInRangeWithZeroRangeReturnsOnlyExactMatches()
        {
            Vector3 target = new(5f, -3f, 2f);

            List<Vector3> points = new() { target, target, target + new Vector3(0.1f, 0f, 0f) };

            TTree tree = CreateTree(points);

            List<Vector3> results = new() { new Vector3(123f, 456f, 789f) };

            tree.GetElementsInRange(target, 0f, results);

            Vector3[] expected = { target, target };

            CollectionAssert.AreEquivalent(expected, results);
        }

        [Test]
        public void GetElementsInRangeWithMinimumRangeGreaterThanRangeReturnsEmpty()
        {
            List<Vector3> points = new() { new Vector3(0f, 0f, 0f), new Vector3(1f, 1f, 1f) };

            TTree tree = CreateTree(points);

            List<Vector3> results = new() { new Vector3(123f, 456f, 789f) };

            tree.GetElementsInRange(Vector3.zero, 2f, results, minimumRange: 5f);

            Assert.IsEmpty(results);
        }

        [Test]
        public void GetElementsInBoundsWithNoIntersectionReturnsEmpty()
        {
            List<Vector3> points = new() { Vector3.zero };

            TTree tree = CreateTree(points);

            Bounds bounds = new(new Vector3(100f, 100f, 100f), new Vector3(1f, 1f, 1f));

            List<Vector3> results = new() { new Vector3(123f, 456f, 789f) };

            tree.GetElementsInBounds(bounds, results);

            Assert.IsEmpty(results);
        }

        [Test]
        public void GetApproximateNearestNeighborsReturnsEmptyWhenCountZero()
        {
            List<Vector3> points = new() { Vector3.zero, new Vector3(1f, 1f, 1f) };

            TTree tree = CreateTree(points);

            Vector3 sentinel = new(123f, 456f, 789f);

            List<Vector3> results = new() { sentinel };

            tree.GetApproximateNearestNeighbors(Vector3.zero, 0, results);

            Assert.IsEmpty(results);
        }

        [Test]
        public void GetApproximateNearestNeighborsOnEmptyTreeReturnsEmpty()
        {
            TTree tree = CreateTree(Enumerable.Empty<Vector3>());

            Vector3 sentinel = new(123f, 456f, 789f);

            List<Vector3> results = new() { sentinel };

            tree.GetApproximateNearestNeighbors(Vector3.zero, 3, results);

            Assert.IsEmpty(results);
        }

        [Test]
        public void GetApproximateNearestNeighborsClearsResultsList()
        {
            List<Vector3> points = new() { Vector3.zero, new Vector3(2f, 0f, 0f) };

            TTree tree = CreateTree(points);

            Vector3 sentinel = new(123f, 456f, 789f);

            List<Vector3> results = new() { sentinel };

            tree.GetApproximateNearestNeighbors(Vector3.zero, 1, results);

            Assert.IsFalse(results.Contains(sentinel));

            Assert.AreEqual(1, results.Count);
        }

        [Test]
        public void AllPointsIdenticalQueries()
        {
            const int count = 96;
            Vector3 repeated = new(4.25f, -1.5f, 2f);
            List<Vector3> points = Enumerable.Repeat(repeated, count).ToList();

            TTree tree = CreateTree(points);

            List<Vector3> rangeResults = new() { new Vector3(1f, 1f, 1f) };
            tree.GetElementsInRange(repeated, 0f, rangeResults);
            Assert.AreEqual(count, rangeResults.Count);
            Assert.IsTrue(rangeResults.TrueForAll(candidate => candidate == repeated));

            Bounds bounds = new(repeated, new Vector3(0.1f, 0.1f, 0.1f));
            List<Vector3> boundsResults = new() { new Vector3(-1f, -1f, -1f) };
            tree.GetElementsInBounds(bounds, boundsResults);
            Assert.AreEqual(count, boundsResults.Count);
            Assert.IsTrue(boundsResults.TrueForAll(candidate => candidate == repeated));

            List<Vector3> neighbors = new();
            tree.GetApproximateNearestNeighbors(repeated, count * 2, neighbors);
            Assert.IsNotEmpty(neighbors);
            Assert.AreEqual(repeated, neighbors[0]);
        }
    }
}
