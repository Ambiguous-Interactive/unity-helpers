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

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class RTree3DTests : SpatialTree3DTests<RTree3D<Vector3>>
    {
        /*
            A fixed seed, not PRNG.Instance: that hands out an instance seeded from Guid.NewGuid(),
            so a failing case cannot be replayed. SetUp reseeds it, which is what makes running one
            test alone produce the data it produced inside the whole fixture.
        */
        private const uint RandomSeed = 0x5EED0302;

        private IRandom _random = new PcgRandom(RandomSeed);

        private IRandom Random => _random;

        [SetUp]
        public void SeedRTree3DRandom()
        {
            _random = new PcgRandom(RandomSeed);
        }

        protected override RTree3D<Vector3> CreateTree(IEnumerable<Vector3> points)
        {
            return new RTree3D<Vector3>(points, CreatePointBounds);
        }

        [Test]
        public void ConstructorWithNullPointsThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
            {
                new RTree3D<Vector3>(null, CreatePointBounds);
            });
        }

        [Test]
        public void ConstructorWithNullTransformerThrowsArgumentNullException()
        {
            List<Vector3> points = new() { Vector3.zero };
            Assert.Throws<ArgumentNullException>(() =>
            {
                new RTree3D<Vector3>(points, null);
            });
        }

        [Test]
        public void ConstructorWithEmptyCollectionSucceeds()
        {
            List<Vector3> points = new();
            RTree3D<Vector3> tree = CreateTree(points);
            Assert.IsTrue(tree != null);

            List<Vector3> results = new();
            tree.GetElementsInBounds(new Bounds(Vector3.zero, Vector3.one * 100f), results);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void ConstructorWithSingleElementSucceeds()
        {
            Vector3 point = new(
                Random.NextFloat(-50, 50),
                Random.NextFloat(-50, 50),
                Random.NextFloat(-50, 50)
            );
            List<Vector3> points = new() { point };
            RTree3D<Vector3> tree = CreateTree(points);

            List<Vector3> results = new();
            tree.GetElementsInBounds(new Bounds(point, Vector3.one), results);
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(point, results[0]);
        }

        [Test]
        public void ConstructorWithDuplicateElementsPreservesAll()
        {
            Vector3 point = new(5f, 5f, 5f);
            List<Vector3> points = new() { point, point, point };
            RTree3D<Vector3> tree = CreateTree(points);

            List<Vector3> results = new();
            tree.GetElementsInBounds(new Bounds(point, Vector3.one * 2f), results);
            Assert.AreEqual(3, results.Count);
        }

        [Test]
        public void GetElementsInBoundsWithEmptyTreeReturnsEmpty()
        {
            RTree3D<Vector3> tree = CreateTree(new List<Vector3>());
            List<Vector3> results = new();

            tree.GetElementsInBounds(new Bounds(Vector3.zero, Vector3.one * 10f), results);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetElementsInBoundsReturnsIntersectingElements()
        {
            List<Vector3> points = new();
            for (int x = 0; x < 5; ++x)
            {
                for (int y = 0; y < 5; ++y)
                {
                    for (int z = 0; z < 5; ++z)
                    {
                        points.Add(new Vector3(x * 2f, y * 2f, z * 2f));
                    }
                }
            }

            RTree3D<Vector3> tree = CreateTree(points);
            Bounds bounds = new(new Vector3(4f, 4f, 4f), new Vector3(6f, 6f, 6f));
            List<Vector3> results = new();

            tree.GetElementsInBounds(bounds, results);
            BoundingBox3D queryBounds = BoundingBox3D.FromClosedBounds(bounds);
            List<Vector3> expected = points.Where(queryBounds.Contains).ToList();
            CollectionAssert.AreEquivalent(expected, results);
        }

        [Test]
        public void GetElementsInBoundsTreatsUpperBoundaryAsInclusive()
        {
            /*
                Matches KdTree3D and OctTree3D, which both convert the query with
                FromClosedBoundsInclusiveMax. A point sitting on the max face is inside the box.
            */
            List<Vector3> points = new() { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f) };

            RTree3D<Vector3> tree = CreateTree(points);
            Bounds bounds = new(
                center: new Vector3(0.5f, 0f, 0f),
                size: new Vector3(1f, 0.1f, 0.1f)
            );
            List<Vector3> results = new();

            tree.GetElementsInBounds(bounds, results);

            CollectionAssert.AreEquivalent(points, results);
        }

        /// <summary>
        /// The element bounds are genuinely zero-size, not the 0.001 cube the shared
        /// <see cref="CreatePointBounds"/> harness inflates points into. That inflation is what let
        /// the old version of this test pass while <c>p =&gt; new Bounds(p, Vector3.zero)</c> -- the
        /// natural point transformer -- came back empty: the element's stored center had been
        /// displaced by the degenerate-box padding, past a query max that is one ULP wide.
        /// </summary>
        [Test]
        public void GetElementsInBoundsWithZeroSizeElementsFindsThePointOnIt()
        {
            List<Vector3> points = new() { new Vector3(2f, -3f, 4f), new Vector3(5f, 5f, 5f) };

            RTree3D<Vector3> tree = new(points, ZeroSizeBounds);
            List<Vector3> results = new();

            tree.GetElementsInBounds(new Bounds(points[0], Vector3.zero), results);

            CollectionAssert.AreEquivalent(new[] { points[0] }, results);

            tree.GetElementsInRange(points[0], 0f, results);

            CollectionAssert.AreEquivalent(new[] { points[0] }, results);
        }

        /// <summary>
        /// A zero-size element sitting exactly on the max face of the query box. The face is
        /// inclusive, so it is in -- and it was the case the padded center missed, by about
        /// 5e-7 units.
        /// </summary>
        [Test]
        public void GetElementsInBoundsWithZeroSizeElementsIncludesTheMaxFace()
        {
            List<Vector3> points = new() { new Vector3(-4f, -4f, -4f), new Vector3(-6f, -6f, -6f) };

            RTree3D<Vector3> tree = new(points, ZeroSizeBounds);
            List<Vector3> results = new();

            tree.GetElementsInBounds(
                new Bounds(new Vector3(-6f, -6f, -6f), new Vector3(4f, 4f, 4f)),
                results
            );

            CollectionAssert.AreEquivalent(points, results);
        }

        /// <summary>
        /// Every axis-aligned neighbor of a grid-aligned sphere query sits exactly on the query
        /// box's max face, so a half-open candidate box drops three of the six.
        /// </summary>
        [Test]
        public void GetElementsInRangeWithZeroSizeElementsIncludesTheRadiusFace()
        {
            List<Vector3> points = new();
            for (int x = -1; x <= 1; ++x)
            {
                for (int y = -1; y <= 1; ++y)
                {
                    for (int z = -1; z <= 1; ++z)
                    {
                        points.Add(new Vector3(x, y, z));
                    }
                }
            }

            RTree3D<Vector3> tree = new(points, ZeroSizeBounds);
            List<Vector3> results = new();

            tree.GetElementsInRange(Vector3.zero, 1f, results);

            Vector3[] expected =
            {
                Vector3.zero,
                new(1f, 0f, 0f),
                new(-1f, 0f, 0f),
                new(0f, 1f, 0f),
                new(0f, -1f, 0f),
                new(0f, 0f, 1f),
                new(0f, 0f, -1f),
            };
            CollectionAssert.AreEquivalent(expected, results);
        }

        [Test]
        public void GetElementsInBoundsWithNoIntersectionReturnsEmptyAdditional()
        {
            List<Vector3> points = new() { new Vector3(-10f, 0f, 0f), new Vector3(10f, 0f, 0f) };

            RTree3D<Vector3> tree = CreateTree(points);
            List<Vector3> results = new();

            tree.GetElementsInBounds(
                new Bounds(new Vector3(100f, 100f, 100f), Vector3.one),
                results
            );
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetElementsInRangeWithEmptyTreeReturnsEmptyAdditional()
        {
            RTree3D<Vector3> tree = CreateTree(new List<Vector3>());
            List<Vector3> results = new();

            tree.GetElementsInRange(Vector3.zero, 10f, results);
            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GetElementsInRangeWithZeroRangeReturnsOnlyExactMatchesAdditional()
        {
            Vector3 target = new(5f, -3f, 2f);
            List<Vector3> points = new() { target, target, target + new Vector3(0.1f, 0f, 0f) };
            RTree3D<Vector3> tree = CreateTree(points);
            List<Vector3> results = new() { new Vector3(999f, 999f, 999f) };

            tree.GetElementsInRange(target, 0f, results);

            Vector3[] expected = { target, target };
            CollectionAssert.AreEquivalent(expected, results);
        }

        [Test]
        public void GetElementsInRangeWithLargeRangeReturnsAllPoints()
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

            RTree3D<Vector3> tree = CreateTree(points);
            List<Vector3> results = new();

            tree.GetElementsInRange(Vector3.zero, 1_000f, results);
            CollectionAssert.AreEquivalent(points, results);
        }

        [Test]
        public void GetElementsInRangeRespectsMinimumRangeAdditional()
        {
            List<Vector3> points = new()
            {
                new Vector3(0f, 0f, 1f),
                new Vector3(0f, 0f, 3f),
                new Vector3(0f, 0f, 5f),
                new Vector3(0f, 0f, 7f),
            };

            RTree3D<Vector3> tree = CreateTree(points);
            List<Vector3> results = new();
            tree.GetElementsInRange(Vector3.zero, 5.5f, results, minimumRange: 2f);

            Vector3[] expected = { points[1], points[2] };
            CollectionAssert.AreEquivalent(expected, results);
        }

        [Test]
        public void GetElementsInRangeClearsResultsListAdditional()
        {
            List<Vector3> points = new() { Vector3.zero };
            RTree3D<Vector3> tree = CreateTree(points);
            Vector3 sentinel = new(-999f, -999f, -999f);
            List<Vector3> results = new() { sentinel };

            tree.GetElementsInRange(Vector3.zero, 10f, results);
            Assert.IsFalse(results.Contains(sentinel));
            CollectionAssert.AreEquivalent(points, results);
        }

        [Test]
        public void GetElementsInBoundsClearsResultsListAdditional2()
        {
            List<Vector3> points = new() { Vector3.zero };
            RTree3D<Vector3> tree = CreateTree(points);
            Vector3 sentinel = new(999f, 999f, 999f);
            List<Vector3> results = new() { sentinel };

            tree.GetElementsInBounds(new Bounds(Vector3.zero, Vector3.one * 2f), results);
            Assert.IsFalse(results.Contains(sentinel));
            CollectionAssert.AreEquivalent(points, results);
        }

        [Test]
        public void HandlesColinearPointsAlongXAxis()
        {
            List<Vector3> points = new();
            for (int i = 0; i < 100; ++i)
            {
                points.Add(new Vector3(i, 0f, 0f));
            }

            RTree3D<Vector3> tree = CreateTree(points);
            List<Vector3> results = new();

            tree.GetElementsInRange(new Vector3(50f, 0f, 0f), 10f, results);
            Assert.Greater(results.Count, 0);
            foreach (Vector3 result in results)
            {
                float distance = Vector3.Distance(result, new Vector3(50f, 0f, 0f));
                Assert.LessOrEqual(distance, 10.01f);
            }
        }

        private static Bounds CreatePointBounds(Vector3 point)
        {
            const float pointBoundsSize = 0.001f;
            return new Bounds(
                point,
                new Vector3(pointBoundsSize, pointBoundsSize, pointBoundsSize)
            );
        }

        private static Bounds ZeroSizeBounds(Vector3 point)
        {
            return new Bounds(point, Vector3.zero);
        }
    }
}
