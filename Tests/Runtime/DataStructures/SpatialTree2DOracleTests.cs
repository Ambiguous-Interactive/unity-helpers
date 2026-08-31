// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;
    using Sample = WallstopStudios.UnityHelpers.Tests.TestUtils.SpatialQueryOracle.Sample;

    /// <summary>
    /// Drives every 2D structure -- both k-d trees, the quad tree and the R-tree -- against the
    /// brute-force oracle over a fixed edge corpus that includes the invalid inputs the query
    /// contract names. Results are compared as multisets, so an implementation that collapses two
    /// equal values fails here rather than passing a set comparison.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SpatialTree2DOracleTests
    {
        [Test]
        [Timeout(120000)]
        public void RangeQueriesMatchOracle()
        {
            foreach (Corpus corpus in Corpora())
            {
                foreach (RangeQuery query in RangeQueries())
                {
                    List<Sample> expected = SpatialQueryOracle.Project(
                        corpus.samples,
                        SpatialQueryOracle.WithinRadius2D(
                            corpus.samples,
                            query.center,
                            query.radius,
                            query.minimumRange
                        )
                    );

                    foreach (Structure structure in Structures(corpus.samples))
                    {
                        List<Sample> actual = new() { Sentinel };
                        structure.tree.GetElementsInRange(
                            query.center,
                            query.radius,
                            actual,
                            query.minimumRange
                        );

                        CollectionAssert.AreEquivalent(
                            expected,
                            actual,
                            "{0} / {1} / {2}",
                            structure.name,
                            corpus.name,
                            query
                        );
                    }
                }
            }
        }

        [Test]
        [Timeout(120000)]
        public void BoundsQueriesMatchOracle()
        {
            foreach (Corpus corpus in Corpora())
            {
                foreach (BoxQuery query in BoxQueries())
                {
                    List<Sample> expected = SpatialQueryOracle.Project(
                        corpus.samples,
                        SpatialQueryOracle.InsideBox2D(corpus.samples, query.minimum, query.maximum)
                    );

                    Bounds bounds = FromCorners(query.minimum, query.maximum);
                    foreach (Structure structure in Structures(corpus.samples))
                    {
                        List<Sample> actual = new() { Sentinel };
                        structure.tree.GetElementsInBounds(bounds, actual);

                        CollectionAssert.AreEquivalent(
                            expected,
                            actual,
                            "{0} / {1} / {2}",
                            structure.name,
                            corpus.name,
                            query
                        );
                    }
                }
            }
        }

        [Test]
        public void NearestNeighborsReturnMinimumOfRequestAndSize()
        {
            Vector2 center = new(0.25f, -0.75f);
            foreach (Corpus corpus in Corpora())
            {
                int size = corpus.samples.Length;
                foreach (int requested in NeighborCounts(size))
                {
                    int expectedCount = requested <= 0 ? 0 : Math.Min(requested, size);
                    foreach (Structure structure in Structures(corpus.samples))
                    {
                        List<Sample> actual = new() { Sentinel };
                        structure.tree.GetApproximateNearestNeighbors(center, requested, actual);

                        Assert.AreEqual(
                            expectedCount,
                            actual.Count,
                            "{0} / {1} / requested {2}",
                            structure.name,
                            corpus.name,
                            requested
                        );

                        AssertOrderedByDistance(actual, center, structure.name, corpus.name);
                        AssertSubMultiset(actual, corpus.samples, structure.name, corpus.name);
                    }
                }
            }
        }

        [Test]
        public void NearestNeighborsRequestingEverythingMatchesOracle()
        {
            Vector2 center = new(0.25f, -0.75f);
            foreach (Corpus corpus in Corpora())
            {
                List<Sample> expected = SpatialQueryOracle.Project(
                    corpus.samples,
                    SpatialQueryOracle.Nearest2D(corpus.samples, center, corpus.samples.Length)
                );

                foreach (Structure structure in Structures(corpus.samples))
                {
                    List<Sample> actual = new() { Sentinel };
                    structure.tree.GetApproximateNearestNeighbors(
                        center,
                        corpus.samples.Length,
                        actual
                    );

                    CollectionAssert.AreEquivalent(
                        expected,
                        actual,
                        "{0} / {1}",
                        structure.name,
                        corpus.name
                    );
                }
            }
        }

        /// <summary>
        /// A small <c>k</c> against a large corpus, where the oracle's sort-and-trim is doing real
        /// work rather than returning everything it was handed. Every structure has to beat the
        /// oracle's <c>k</c> farthest, so an implementation that returned those -- sorted ascending,
        /// the right count, every element genuinely in the tree -- fails here even though it passes
        /// the count, ordering and sub-multiset assertions above.
        /// </summary>
        [Test]
        public void NearestNeighborsBeatTheFarthestCandidates()
        {
            const int requested = 4;
            Sample[] samples = NearestCorpus();
            Vector2 center = NearestCenter;

            List<Sample> farthest = SpatialQueryOracle.Project(
                samples,
                SpatialQueryOracle.Farthest2D(samples, center, requested)
            );

            float closestOfTheFarthest = float.PositiveInfinity;
            foreach (Sample sample in farthest)
            {
                float distanceSquared = DistanceSquared(sample, center);
                if (distanceSquared < closestOfTheFarthest)
                {
                    closestOfTheFarthest = distanceSquared;
                }
            }

            foreach (Structure structure in Structures(samples))
            {
                List<Sample> actual = new() { Sentinel };
                structure.tree.GetApproximateNearestNeighbors(center, requested, actual);

                Assert.AreEqual(requested, actual.Count, structure.name);
                foreach (Sample sample in actual)
                {
                    Assert.Less(
                        DistanceSquared(sample, center),
                        closestOfTheFarthest,
                        "{0}: {1} is one of the {2} farthest samples, not one of the nearest",
                        structure.name,
                        sample,
                        requested
                    );
                }
            }
        }

        /// <summary>
        /// The R-tree's best-first descent is exact, not approximate: it expands nodes in order of
        /// their distance to the query and stops only when the nearest unexpanded node is no closer
        /// than the worst candidate held. So for a corpus with no distance tie at the boundary its
        /// answer has to be the oracle's <c>k</c> nearest, element for element. The k-d and quad
        /// trees follow one greedy path instead and make no such promise -- measured, <c>KdTree2D</c>
        /// answers <c>k = 1</c> on the grid corpus with the third-nearest point -- which is why this
        /// case names the R-tree rather than asserting exactness for everything.
        /// </summary>
        [Test]
        public void RTreeNearestNeighborsMatchTheOracleExactly()
        {
            Sample[] samples = NearestCorpus();
            Vector2 center = NearestCenter;

            for (int requested = 1; requested <= 5; ++requested)
            {
                List<Sample> expected = SpatialQueryOracle.Project(
                    samples,
                    SpatialQueryOracle.Nearest2D(samples, center, requested)
                );

                RTree2D<Sample> tree = new(samples, ToPointBounds);
                List<Sample> actual = new() { Sentinel };
                tree.GetApproximateNearestNeighbors(center, requested, actual);

                CollectionAssert.AreEqual(expected, actual, "RTree2D / requested {0}", requested);
            }
        }

        [Test]
        public void NonFiniteQueriesClearAndReturnEmpty()
        {
            Sample[] samples = GridCorpus();
            float[] badRanges = { float.NaN, -1f, float.NegativeInfinity };
            Vector2[] badCenters =
            {
                new(float.NaN, 0f),
                new(0f, float.NaN),
                new(float.PositiveInfinity, 0f),
            };

            foreach (Structure structure in Structures(samples))
            {
                foreach (float range in badRanges)
                {
                    List<Sample> results = new() { Sentinel };
                    structure.tree.GetElementsInRange(Vector2.zero, range, results);
                    CollectionAssert.IsEmpty(results, "{0} / range {1}", structure.name, range);
                }

                foreach (Vector2 center in badCenters)
                {
                    List<Sample> results = new() { Sentinel };
                    structure.tree.GetElementsInRange(center, 5f, results);
                    CollectionAssert.IsEmpty(results, "{0} / center {1}", structure.name, center);

                    List<Sample> neighbors = new() { Sentinel };
                    structure.tree.GetApproximateNearestNeighbors(center, 3, neighbors);
                    CollectionAssert.IsEmpty(
                        neighbors,
                        "{0} / neighbors of {1}",
                        structure.name,
                        center
                    );
                }

                List<Sample> boundsResults = new() { Sentinel };
                structure.tree.GetElementsInBounds(
                    new Bounds(new Vector3(float.NaN, 0f, 0f), Vector3.one),
                    boundsResults
                );
                CollectionAssert.IsEmpty(boundsResults, "{0} / NaN bounds", structure.name);
            }
        }

        [Test]
        public void NullDestinationThrowsArgumentNullException()
        {
            Sample[] samples = GridCorpus();
            foreach (Structure structure in Structures(samples))
            {
                ISpatialTree2D<Sample> tree = structure.tree;
                Assert.Throws<ArgumentNullException>(
                    () => tree.GetElementsInRange(Vector2.zero, 1f, null),
                    structure.name
                );
                Assert.Throws<ArgumentNullException>(
                    () => tree.GetElementsInBounds(new Bounds(Vector3.zero, Vector3.one), null),
                    structure.name
                );
                Assert.Throws<ArgumentNullException>(
                    () => tree.GetApproximateNearestNeighbors(Vector2.zero, 1, null),
                    structure.name
                );
            }
        }

        private static Sample Sentinel => new(new Vector3(987f, 654f, 321f), -99, -1);

        private static Vector2 NearestCenter => new(0.25f, -0.75f);

        private static float DistanceSquared(Sample sample, Vector2 center)
        {
            float deltaX = sample.position.x - center.x;
            float deltaY = sample.position.y - center.y;
            return (deltaX * deltaX) + (deltaY * deltaY);
        }

        private static void AssertOrderedByDistance(
            List<Sample> results,
            Vector2 center,
            string structureName,
            string corpusName
        )
        {
            float previous = float.NegativeInfinity;
            for (int i = 0; i < results.Count; ++i)
            {
                float distanceSquared = DistanceSquared(results[i], center);
                Assert.LessOrEqual(
                    previous,
                    distanceSquared,
                    "{0} / {1}: neighbor {2} is closer than its predecessor",
                    structureName,
                    corpusName,
                    i
                );
                previous = distanceSquared;
            }
        }

        private static void AssertSubMultiset(
            List<Sample> results,
            Sample[] samples,
            string structureName,
            string corpusName
        )
        {
            Dictionary<Sample, int> available = new();
            foreach (Sample sample in samples)
            {
                if (!available.TryGetValue(sample, out int existing))
                {
                    existing = 0;
                }

                available[sample] = existing + 1;
            }

            foreach (Sample result in results)
            {
                if (!available.TryGetValue(result, out int remaining))
                {
                    remaining = 0;
                }

                Assert.Less(
                    0,
                    remaining,
                    "{0} / {1}: {2} was returned more often than it was inserted",
                    structureName,
                    corpusName,
                    result
                );
                available[result] = remaining - 1;
            }
        }

        private static Bounds FromCorners(Vector2 minimum, Vector2 maximum)
        {
            Vector3 center = new(
                (minimum.x + maximum.x) * 0.5f,
                (minimum.y + maximum.y) * 0.5f,
                0f
            );
            Vector3 size = new(maximum.x - minimum.x, maximum.y - minimum.y, 1f);
            return new Bounds(center, size);
        }

        private static IEnumerable<int> NeighborCounts(int size)
        {
            yield return 0;
            yield return 1;
            if (0 < size)
            {
                yield return size;
            }

            yield return size + 1;
        }

        private static IEnumerable<Structure> Structures(Sample[] samples)
        {
            yield return new Structure(
                "KdTree2D balanced",
                new KdTree2D<Sample>(samples, ToPosition, balanced: true)
            );
            yield return new Structure(
                "KdTree2D unbalanced",
                new KdTree2D<Sample>(samples, ToPosition, balanced: false)
            );
            yield return new Structure("QuadTree2D", new QuadTree2D<Sample>(samples, ToPosition));
            yield return new Structure("RTree2D", new RTree2D<Sample>(samples, ToPointBounds));
        }

        private static Vector2 ToPosition(Sample sample)
        {
            return sample.Position2D;
        }

        /// <summary>
        /// The natural point transformer: a zero-size box whose center is the sample. The R-trees
        /// index boxes, so this is what makes their answers comparable with the point trees' -- and
        /// it is the shape that caught <c>RTree3D</c> dropping a point on a zero-size query box.
        /// </summary>
        private static Bounds ToPointBounds(Sample sample)
        {
            return new Bounds(new Vector3(sample.position.x, sample.position.y, 0f), Vector3.zero);
        }

        private static IEnumerable<RangeQuery> RangeQueries()
        {
            yield return new RangeQuery(Vector2.zero, 0f, 0f);
            yield return new RangeQuery(new Vector2(2f, 2f), 0f, 0f);
            yield return new RangeQuery(Vector2.zero, 1f, 0f);
            yield return new RangeQuery(Vector2.zero, 2f, 0f);
            yield return new RangeQuery(new Vector2(-3f, -3f), 4f, 0f);
            yield return new RangeQuery(Vector2.zero, 2f, 1f);
            yield return new RangeQuery(Vector2.zero, 1000f, 0f);
            yield return new RangeQuery(new Vector2(1e18f, 0f), 1.5e18f, 0f);
            yield return new RangeQuery(Vector2.zero, float.NaN, 0f);
            yield return new RangeQuery(Vector2.zero, -1f, 0f);
            yield return new RangeQuery(Vector2.zero, float.NegativeInfinity, 0f);
            yield return new RangeQuery(new Vector2(float.NaN, 0f), 2f, 0f);
            yield return new RangeQuery(new Vector2(0f, float.PositiveInfinity), 2f, 0f);
        }

        private static IEnumerable<BoxQuery> BoxQueries()
        {
            yield return new BoxQuery(new Vector2(-2f, -2f), new Vector2(2f, 2f));
            yield return new BoxQuery(new Vector2(0f, 0f), new Vector2(0f, 0f));
            yield return new BoxQuery(new Vector2(2f, 2f), new Vector2(2f, 2f));
            yield return new BoxQuery(new Vector2(-8f, -8f), new Vector2(-4f, -4f));
            yield return new BoxQuery(new Vector2(-1024f, -1024f), new Vector2(1024f, 1024f));
            yield return new BoxQuery(new Vector2(float.NaN, -2f), new Vector2(2f, 2f));
            yield return new BoxQuery(new Vector2(-2f, -2f), new Vector2(2f, float.NaN));
            yield return new BoxQuery(new Vector2(2f, 2f), new Vector2(-2f, -2f));
        }

        private static IEnumerable<Corpus> Corpora()
        {
            yield return new Corpus("empty", Array.Empty<Sample>());
            yield return new Corpus(
                "singleton",
                new[] { new Sample(new Vector3(3f, -7f, 0f), 5, 0) }
            );
            yield return new Corpus("duplicates", DuplicateCorpus());
            yield return new Corpus("grid", GridCorpus());
            yield return new Corpus("negative", NegativeCorpus());
            yield return new Corpus("huge", HugeCorpus());
        }

        private static Sample[] DuplicateCorpus()
        {
            return new[]
            {
                new Sample(new Vector3(2f, 2f, 0f), 1, 0),
                new Sample(new Vector3(2f, 2f, 0f), 1, 1),
                new Sample(new Vector3(2f, 2f, 0f), 1, 2),
                new Sample(new Vector3(-5f, 4f, 0f), 2, 3),
                new Sample(new Vector3(-5f, 4f, 0f), 2, 4),
                new Sample(Vector3.zero, 3, 5),
            };
        }

        private static Sample[] GridCorpus()
        {
            List<Sample> samples = new();
            int insertionIndex = 0;
            for (int x = -2; x <= 2; ++x)
            {
                for (int y = -2; y <= 2; ++y)
                {
                    samples.Add(new Sample(new Vector3(x, y, 0f), x + y, insertionIndex));
                    ++insertionIndex;
                }
            }

            return samples.ToArray();
        }

        /// <summary>
        /// Sixteen samples whose squared distances to <see cref="NearestCenter"/> are all distinct,
        /// so "the k nearest" is one answer rather than a family of tied ones.
        /// </summary>
        private static Sample[] NearestCorpus()
        {
            List<Sample> samples = new();
            int insertionIndex = 0;
            for (int i = 0; i < 16; ++i)
            {
                float offset = 0.5f + (i * 1.25f);
                samples.Add(
                    new Sample(
                        new Vector3(
                            NearestCenter.x + offset,
                            NearestCenter.y + (offset * 0.5f),
                            0f
                        ),
                        i,
                        insertionIndex
                    )
                );
                ++insertionIndex;
            }

            return samples.ToArray();
        }

        private static Sample[] NegativeCorpus()
        {
            return new[]
            {
                new Sample(new Vector3(-1f, -1f, 0f), 7, 0),
                new Sample(new Vector3(-4f, -4f, 0f), 7, 1),
                new Sample(new Vector3(-6f, -6f, 0f), 8, 2),
                new Sample(new Vector3(-0.5f, -0.5f, 0f), 9, 3),
            };
        }

        private static Sample[] HugeCorpus()
        {
            return new[]
            {
                new Sample(Vector3.zero, 0, 0),
                new Sample(new Vector3(1e18f, 0f, 0f), 1, 1),
                new Sample(new Vector3(-1e18f, 0f, 0f), 1, 2),
                new Sample(new Vector3(3e18f, 0f, 0f), 2, 3),
            };
        }

        private readonly struct Structure
        {
            internal readonly string name;
            internal readonly ISpatialTree2D<Sample> tree;

            internal Structure(string name, ISpatialTree2D<Sample> tree)
            {
                this.name = name;
                this.tree = tree;
            }
        }

        private readonly struct Corpus
        {
            internal readonly string name;
            internal readonly Sample[] samples;

            internal Corpus(string name, Sample[] samples)
            {
                this.name = name;
                this.samples = samples;
            }
        }

        private readonly struct RangeQuery
        {
            internal readonly Vector2 center;
            internal readonly float radius;
            internal readonly float minimumRange;

            internal RangeQuery(Vector2 center, float radius, float minimumRange)
            {
                this.center = center;
                this.radius = radius;
                this.minimumRange = minimumRange;
            }

            public override string ToString()
            {
                return $"range(center: {center}, radius: {radius}, minimum: {minimumRange})";
            }
        }

        private readonly struct BoxQuery
        {
            internal readonly Vector2 minimum;
            internal readonly Vector2 maximum;

            internal BoxQuery(Vector2 minimum, Vector2 maximum)
            {
                this.minimum = minimum;
                this.maximum = maximum;
            }

            public override string ToString()
            {
                return $"box(min: {minimum}, max: {maximum})";
            }
        }
    }
}
