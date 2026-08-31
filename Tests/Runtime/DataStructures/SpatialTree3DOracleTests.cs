// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using Sample = WallstopStudios.UnityHelpers.Tests.DataStructures.SpatialQueryOracle.Sample;

    /// <summary>
    /// Drives every 3D point tree against the brute-force oracle over a fixed edge corpus.
    /// Results are compared as multisets, so an implementation that collapses two equal values
    /// fails here rather than passing a set comparison.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SpatialTree3DOracleTests
    {
        [Test]
        public void RangeQueriesMatchOracle()
        {
            foreach (Corpus corpus in Corpora())
            {
                foreach (RangeQuery query in RangeQueries())
                {
                    List<Sample> expected = SpatialQueryOracle.Project(
                        corpus.samples,
                        SpatialQueryOracle.WithinRadius3D(
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
        public void BoundsQueriesMatchOracle()
        {
            foreach (Corpus corpus in Corpora())
            {
                foreach (BoxQuery query in BoxQueries())
                {
                    List<Sample> expected = SpatialQueryOracle.Project(
                        corpus.samples,
                        SpatialQueryOracle.InsideBox3D(corpus.samples, query.minimum, query.maximum)
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
            Vector3 center = new(0.25f, -0.75f, 0.5f);
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
                    }
                }
            }
        }

        [Test]
        public void NearestNeighborsRequestingEverythingMatchesOracle()
        {
            Vector3 center = new(0.25f, -0.75f, 0.5f);
            foreach (Corpus corpus in Corpora())
            {
                List<Sample> expected = SpatialQueryOracle.Project(
                    corpus.samples,
                    SpatialQueryOracle.Nearest3D(corpus.samples, center, corpus.samples.Length)
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

        [Test]
        public void NonFiniteQueriesClearAndReturnEmpty()
        {
            Sample[] samples = GridCorpus();
            float[] badRanges = { float.NaN, -1f, float.NegativeInfinity };
            Vector3[] badCenters =
            {
                new(float.NaN, 0f, 0f),
                new(0f, float.NaN, 0f),
                new(0f, 0f, float.PositiveInfinity),
            };

            foreach (Structure structure in Structures(samples))
            {
                foreach (float range in badRanges)
                {
                    List<Sample> results = new() { Sentinel };
                    structure.tree.GetElementsInRange(Vector3.zero, range, results);
                    CollectionAssert.IsEmpty(results, "{0} / range {1}", structure.name, range);
                }

                foreach (Vector3 center in badCenters)
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
                ISpatialTree3D<Sample> tree = structure.tree;
                Assert.Throws<ArgumentNullException>(
                    () => tree.GetElementsInRange(Vector3.zero, 1f, null),
                    structure.name
                );
                Assert.Throws<ArgumentNullException>(
                    () => tree.GetElementsInBounds(new Bounds(Vector3.zero, Vector3.one), null),
                    structure.name
                );
                Assert.Throws<ArgumentNullException>(
                    () => tree.GetApproximateNearestNeighbors(Vector3.zero, 1, null),
                    structure.name
                );
            }
        }

        private static Sample Sentinel => new(new Vector3(987f, 654f, 321f), -99, -1);

        private static void AssertOrderedByDistance(
            List<Sample> results,
            Vector3 center,
            string structureName,
            string corpusName
        )
        {
            float previous = float.NegativeInfinity;
            for (int i = 0; i < results.Count; ++i)
            {
                Vector3 position = results[i].position;
                float deltaX = position.x - center.x;
                float deltaY = position.y - center.y;
                float deltaZ = position.z - center.z;
                float distanceSquared = (deltaX * deltaX) + (deltaY * deltaY) + (deltaZ * deltaZ);
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

        private static Bounds FromCorners(Vector3 minimum, Vector3 maximum)
        {
            Vector3 center = (minimum + maximum) * 0.5f;
            Vector3 size = maximum - minimum;
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
                "KdTree3D balanced",
                new KdTree3D<Sample>(samples, ToPosition, balanced: true)
            );
            yield return new Structure(
                "KdTree3D unbalanced",
                new KdTree3D<Sample>(samples, ToPosition, balanced: false)
            );
            yield return new Structure("OctTree3D", new OctTree3D<Sample>(samples, ToPosition));
        }

        private static Vector3 ToPosition(Sample sample)
        {
            return sample.position;
        }

        private static IEnumerable<RangeQuery> RangeQueries()
        {
            yield return new RangeQuery(Vector3.zero, 0f, 0f);
            yield return new RangeQuery(new Vector3(2f, 2f, 2f), 0f, 0f);
            yield return new RangeQuery(Vector3.zero, 1f, 0f);
            yield return new RangeQuery(Vector3.zero, 2f, 0f);
            yield return new RangeQuery(new Vector3(-3f, -3f, -3f), 4f, 0f);
            yield return new RangeQuery(Vector3.zero, 2f, 1f);
            yield return new RangeQuery(Vector3.zero, 1000f, 0f);
        }

        private static IEnumerable<BoxQuery> BoxQueries()
        {
            yield return new BoxQuery(new Vector3(-2f, -2f, -2f), new Vector3(2f, 2f, 2f));
            yield return new BoxQuery(Vector3.zero, Vector3.zero);
            yield return new BoxQuery(new Vector3(1f, 1f, 1f), new Vector3(1f, 1f, 1f));
            yield return new BoxQuery(new Vector3(-8f, -8f, -8f), new Vector3(-4f, -4f, -4f));
            yield return new BoxQuery(
                new Vector3(-1024f, -1024f, -1024f),
                new Vector3(1024f, 1024f, 1024f)
            );
        }

        private static IEnumerable<Corpus> Corpora()
        {
            yield return new Corpus("empty", Array.Empty<Sample>());
            yield return new Corpus(
                "singleton",
                new[] { new Sample(new Vector3(3f, -7f, 4f), 5, 0) }
            );
            yield return new Corpus("duplicates", DuplicateCorpus());
            yield return new Corpus("grid", GridCorpus());
            yield return new Corpus("negative", NegativeCorpus());
        }

        private static Sample[] DuplicateCorpus()
        {
            return new[]
            {
                new Sample(new Vector3(1f, 1f, 1f), 1, 0),
                new Sample(new Vector3(1f, 1f, 1f), 1, 1),
                new Sample(new Vector3(1f, 1f, 1f), 1, 2),
                new Sample(new Vector3(-5f, 4f, -2f), 2, 3),
                new Sample(new Vector3(-5f, 4f, -2f), 2, 4),
                new Sample(Vector3.zero, 3, 5),
            };
        }

        private static Sample[] GridCorpus()
        {
            List<Sample> samples = new();
            int insertionIndex = 0;
            for (int x = -1; x <= 1; ++x)
            {
                for (int y = -1; y <= 1; ++y)
                {
                    for (int z = -1; z <= 1; ++z)
                    {
                        samples.Add(new Sample(new Vector3(x, y, z), x + y + z, insertionIndex));
                        ++insertionIndex;
                    }
                }
            }

            return samples.ToArray();
        }

        private static Sample[] NegativeCorpus()
        {
            return new[]
            {
                new Sample(new Vector3(-1f, -1f, -1f), 7, 0),
                new Sample(new Vector3(-4f, -4f, -4f), 7, 1),
                new Sample(new Vector3(-6f, -6f, -6f), 8, 2),
                new Sample(new Vector3(-0.5f, -0.5f, -0.5f), 9, 3),
            };
        }

        private readonly struct Structure
        {
            internal readonly string name;
            internal readonly ISpatialTree3D<Sample> tree;

            internal Structure(string name, ISpatialTree3D<Sample> tree)
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
            internal readonly Vector3 center;
            internal readonly float radius;
            internal readonly float minimumRange;

            internal RangeQuery(Vector3 center, float radius, float minimumRange)
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
            internal readonly Vector3 minimum;
            internal readonly Vector3 maximum;

            internal BoxQuery(Vector3 minimum, Vector3 maximum)
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
