// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Utils;
    using Sample = WallstopStudios.UnityHelpers.Tests.DataStructures.SpatialQueryOracle.Sample;

    /// <summary>
    /// Drives both spatial hashes against the brute-force oracle over a fixed edge corpus, and pins
    /// the contract the grid traversal has to keep: every query terminates, every destination is
    /// cleared once, and disposing one hash leaves its peers alone.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SpatialHashOracleTests
    {
        private readonly List<IDisposable> _trackedResources = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _trackedResources.Count; ++i)
            {
                _trackedResources[i]?.Dispose();
            }

            _trackedResources.Clear();
        }

        [Test]
        public void RadiusQueriesMatchOracleInTwoDimensions()
        {
            foreach (Corpus corpus in Corpora())
            {
                foreach (float cellSize in CellSizes())
                {
                    SpatialHash2D<Sample> hash = Build2D(corpus.samples, cellSize);
                    foreach (RangeQuery query in RangeQueries())
                    {
                        List<Sample> expected = SpatialQueryOracle.Project(
                            corpus.samples,
                            SpatialQueryOracle.WithinRadius2D(
                                corpus.samples,
                                query.center,
                                query.radius
                            )
                        );

                        List<Sample> actual = new() { Sentinel };
                        hash.Query(query.center, query.radius, actual, distinct: false);
                        CollectionAssert.AreEquivalent(
                            expected,
                            actual,
                            "2D multiset / {0} / cell {1} / {2}",
                            corpus.name,
                            cellSize,
                            query
                        );

                        List<Sample> distinctActual = new() { Sentinel };
                        hash.Query(query.center, query.radius, distinctActual, distinct: true);
                        CollectionAssert.AreEquivalent(
                            expected.Distinct().ToList(),
                            distinctActual,
                            "2D distinct / {0} / cell {1} / {2}",
                            corpus.name,
                            cellSize,
                            query
                        );
                    }
                }
            }
        }

        [Test]
        public void RadiusQueriesMatchOracleInThreeDimensions()
        {
            foreach (Corpus corpus in Corpora())
            {
                foreach (float cellSize in CellSizes())
                {
                    SpatialHash3D<Sample> hash = Build3D(corpus.samples, cellSize);
                    foreach (RangeQuery query in RangeQueries())
                    {
                        Vector3 center = new(query.center.x, query.center.y, 0f);
                        List<Sample> expected = SpatialQueryOracle.Project(
                            corpus.samples,
                            SpatialQueryOracle.WithinRadius3D(corpus.samples, center, query.radius)
                        );

                        List<Sample> actual = new() { Sentinel };
                        hash.Query(center, query.radius, actual, distinct: false);
                        CollectionAssert.AreEquivalent(
                            expected,
                            actual,
                            "3D multiset / {0} / cell {1} / {2}",
                            corpus.name,
                            cellSize,
                            query
                        );
                    }
                }
            }
        }

        [Test]
        public void RectangleAndBoxQueriesMatchOracle()
        {
            foreach (Corpus corpus in Corpora())
            {
                foreach (float cellSize in CellSizes())
                {
                    SpatialHash2D<Sample> hash2D = Build2D(corpus.samples, cellSize);
                    SpatialHash3D<Sample> hash3D = Build3D(corpus.samples, cellSize);
                    foreach (BoxQuery query in BoxQueries())
                    {
                        List<Sample> expected2D = SpatialQueryOracle.Project(
                            corpus.samples,
                            SpatialQueryOracle.InsideBox2D(
                                corpus.samples,
                                query.minimum,
                                query.maximum
                            )
                        );

                        List<Sample> actual2D = new() { Sentinel };
                        hash2D.QueryRect(
                            new Rect(
                                query.minimum.x,
                                query.minimum.y,
                                query.maximum.x - query.minimum.x,
                                query.maximum.y - query.minimum.y
                            ),
                            actual2D,
                            distinct: false
                        );
                        CollectionAssert.AreEquivalent(
                            expected2D,
                            actual2D,
                            "2D rect / {0} / cell {1} / {2}",
                            corpus.name,
                            cellSize,
                            query
                        );

                        Vector3 minimum = new(query.minimum.x, query.minimum.y, -0.5f);
                        Vector3 maximum = new(query.maximum.x, query.maximum.y, 0.5f);
                        List<Sample> expected3D = SpatialQueryOracle.Project(
                            corpus.samples,
                            SpatialQueryOracle.InsideBox3D(corpus.samples, minimum, maximum)
                        );

                        List<Sample> actual3D = new() { Sentinel };
                        hash3D.QueryBox(
                            new Bounds((minimum + maximum) * 0.5f, maximum - minimum),
                            actual3D,
                            distinct: false
                        );
                        CollectionAssert.AreEquivalent(
                            expected3D,
                            actual3D,
                            "3D box / {0} / cell {1} / {2}",
                            corpus.name,
                            cellSize,
                            query
                        );
                    }
                }
            }
        }

        [Test]
        public void CoarseQueriesReturnASupersetOfTheExactAnswer()
        {
            Sample[] samples = GridCorpus();
            SpatialHash2D<Sample> hash = Build2D(samples, 1f);

            List<Sample> exact = new();
            hash.Query(Vector2.zero, 1.5f, exact, distinct: false);

            List<Sample> coarse = new();
            hash.Query(Vector2.zero, 1.5f, coarse, distinct: false, exactDistance: false);

            CollectionAssert.IsSubsetOf(exact, coarse);
            Assert.LessOrEqual(exact.Count, coarse.Count);
        }

        [Test]
        public void ConstructorRejectsNonFiniteCellSize()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpatialHash2D<int>(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SpatialHash2D<int>(float.PositiveInfinity)
            );
            Assert.Throws<ArgumentOutOfRangeException>(() => new SpatialHash3D<int>(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new SpatialHash3D<int>(float.PositiveInfinity)
            );
        }

        [Test]
        public void InsertRejectsNonFinitePositionsWithoutMutating()
        {
            SpatialHash2D<int> hash2D = Track(new SpatialHash2D<int>(1f));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                hash2D.Insert(new Vector2(float.NaN, 0f), 1)
            );
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                hash2D.Insert(new Vector2(0f, float.PositiveInfinity), 2)
            );
            Assert.AreEqual(0, hash2D.CellCount);

            SpatialHash3D<int> hash3D = Track(new SpatialHash3D<int>(1f));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                hash3D.Insert(new Vector3(0f, 0f, float.NaN), 1)
            );
            Assert.AreEqual(0, hash3D.CellCount);
        }

        [Test]
        public void InvalidQueriesClearAndReturnEmpty()
        {
            SpatialHash2D<Sample> hash2D = Build2D(GridCorpus(), 1f);
            SpatialHash3D<Sample> hash3D = Build3D(GridCorpus(), 1f);

            float[] badRadii = { float.NaN, -1f, -0.5f, float.NegativeInfinity };
            foreach (float radius in badRadii)
            {
                List<Sample> results = new() { Sentinel };
                hash2D.Query(Vector2.zero, radius, results);
                CollectionAssert.IsEmpty(results, "2D radius {0}", radius);

                List<Sample> results3D = new() { Sentinel };
                hash3D.Query(Vector3.zero, radius, results3D);
                CollectionAssert.IsEmpty(results3D, "3D radius {0}", radius);
            }

            List<Sample> nanCenter = new() { Sentinel };
            hash2D.Query(new Vector2(float.NaN, 0f), 5f, nanCenter);
            CollectionAssert.IsEmpty(nanCenter);

            List<Sample> nanRect = new() { Sentinel };
            hash2D.QueryRect(new Rect(float.NaN, 0f, 1f, 1f), nanRect);
            CollectionAssert.IsEmpty(nanRect);

            List<Sample> invertedRect = new() { Sentinel };
            hash2D.QueryRect(new Rect(2f, 2f, -4f, -4f), invertedRect);
            CollectionAssert.IsEmpty(invertedRect);

            List<Sample> nanBox = new() { Sentinel };
            hash3D.QueryBox(new Bounds(new Vector3(float.NaN, 0f, 0f), Vector3.one), nanBox);
            CollectionAssert.IsEmpty(nanBox);
        }

        [Test]
        public void NullDestinationThrowsArgumentNullException()
        {
            SpatialHash2D<int> hash2D = Track(new SpatialHash2D<int>(1f));
            SpatialHash3D<int> hash3D = Track(new SpatialHash3D<int>(1f));

            Assert.Throws<ArgumentNullException>(() => hash2D.Query(Vector2.zero, 1f, null));
            Assert.Throws<ArgumentNullException>(() =>
                hash2D.QueryRect(new Rect(0f, 0f, 1f, 1f), null)
            );
            Assert.Throws<ArgumentNullException>(() => hash3D.Query(Vector3.zero, 1f, null));
            Assert.Throws<ArgumentNullException>(() =>
                hash3D.QueryBox(new Bounds(Vector3.zero, Vector3.one), null)
            );
        }

        [Test]
        [Timeout(30000)]
        public void HugeAndInfiniteQueriesFinishWithinABudget()
        {
            const long budgetMilliseconds = 5000;
            SpatialHash2D<int> hash2D = Track(new SpatialHash2D<int>(0.001f));
            SpatialHash3D<int> hash3D = Track(new SpatialHash3D<int>(0.001f));
            for (int i = 0; i < 128; ++i)
            {
                hash2D.Insert(new Vector2(i, -i), i);
                hash3D.Insert(new Vector3(i, -i, i), i);
            }

            List<int> results = new();
            Stopwatch stopwatch = Stopwatch.StartNew();

            hash2D.Query(Vector2.zero, float.MaxValue, results);
            Assert.AreEqual(128, results.Count);

            hash2D.Query(Vector2.zero, float.PositiveInfinity, results);
            Assert.AreEqual(128, results.Count);

            hash2D.QueryRect(new Rect(-1e30f, -1e30f, 2e30f, 2e30f), results);
            Assert.AreEqual(128, results.Count);

            hash3D.Query(Vector3.zero, float.MaxValue, results);
            Assert.AreEqual(128, results.Count);

            hash3D.Query(Vector3.zero, float.PositiveInfinity, results);
            Assert.AreEqual(128, results.Count);

            hash3D.QueryBox(new Bounds(Vector3.zero, Vector3.one * 2e30f), results);
            Assert.AreEqual(128, results.Count);

            stopwatch.Stop();
            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                budgetMilliseconds,
                "Unbounded-radius queries took {0} ms; a dense-cell traversal regressed.",
                stopwatch.ElapsedMilliseconds
            );
        }

        [Test]
        [Timeout(30000)]
        public void TinyCellSizeWithLargeRadiusFinishesWithinABudget()
        {
            const long budgetMilliseconds = 5000;
            SpatialHash2D<int> hash = Track(new SpatialHash2D<int>(float.Epsilon));
            hash.Insert(Vector2.zero, 1);
            hash.Insert(new Vector2(4f, 4f), 2);

            List<int> results = new();
            Stopwatch stopwatch = Stopwatch.StartNew();
            hash.Query(Vector2.zero, 1000f, results);
            stopwatch.Stop();

            CollectionAssert.AreEquivalent(new[] { 1, 2 }, results);
            Assert.Less(
                stopwatch.ElapsedMilliseconds,
                budgetMilliseconds,
                "A float.Epsilon cell size took {0} ms; the dense-cell fallback regressed.",
                stopwatch.ElapsedMilliseconds
            );
        }

        [Test]
        public void DisposingOneHashLeavesPeersAndTheSharedPoolAlone()
        {
            IEqualityComparer<int> shared = EqualityComparer<int>.Default;
            WallstopGenericPool<HashSet<int>> poolBefore = SetBuffers<int>.GetHashSetPool(shared);

            SpatialHash2D<int> first = new(1f);
            SpatialHash2D<int> second = Track(new SpatialHash2D<int>(1f));
            SpatialHash3D<int> third = Track(new SpatialHash3D<int>(1f));
            second.Insert(new Vector2(0.5f, 0.5f), 7);
            third.Insert(new Vector3(0.5f, 0.5f, 0.5f), 9);

            first.Dispose();

            Assert.IsTrue(
                SetBuffers<int>.HasHashSetPool(shared),
                "Disposing one spatial hash destroyed the process-wide HashSet pool."
            );
            Assert.AreSame(poolBefore, SetBuffers<int>.GetHashSetPool(shared));

            List<int> results = new();
            second.Query(new Vector2(0.5f, 0.5f), 1f, results);
            CollectionAssert.AreEqual(new[] { 7 }, results);

            third.Query(new Vector3(0.5f, 0.5f, 0.5f), 1f, results);
            CollectionAssert.AreEqual(new[] { 9 }, results);
        }

        [Test]
        public void SaturatedCellCoordinatesStillAnswerExactly()
        {
            // 1e18 and 3e18 both saturate onto cell int.MaxValue and share one bucket; the
            // exact-distance filter is what still separates them.
            SpatialHash2D<int> hash = Track(new SpatialHash2D<int>(1f));
            hash.Insert(new Vector2(1e18f, 0f), 1);
            hash.Insert(new Vector2(3e18f, 0f), 2);
            hash.Insert(Vector2.zero, 3);

            List<int> results = new();
            hash.Query(new Vector2(1e18f, 0f), 1f, results);
            CollectionAssert.AreEqual(new[] { 1 }, results);

            hash.Query(Vector2.zero, 1f, results);
            CollectionAssert.AreEqual(new[] { 3 }, results);

            hash.Query(Vector2.zero, 2e18f, results);
            CollectionAssert.AreEquivalent(new[] { 1, 3 }, results);

            hash.Query(Vector2.zero, 4e18f, results);
            CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, results);
        }

        private static Sample Sentinel => new(new Vector3(987f, 654f, 321f), -99, -1);

        private T Track<T>(T disposable)
            where T : IDisposable
        {
            _trackedResources.Add(disposable);
            return disposable;
        }

        private SpatialHash2D<Sample> Build2D(Sample[] samples, float cellSize)
        {
            SpatialHash2D<Sample> hash = Track(new SpatialHash2D<Sample>(cellSize));
            foreach (Sample sample in samples)
            {
                hash.Insert(sample.Position2D, sample);
            }

            return hash;
        }

        private SpatialHash3D<Sample> Build3D(Sample[] samples, float cellSize)
        {
            SpatialHash3D<Sample> hash = Track(new SpatialHash3D<Sample>(cellSize));
            foreach (Sample sample in samples)
            {
                hash.Insert(sample.position, sample);
            }

            return hash;
        }

        private static IEnumerable<float> CellSizes()
        {
            yield return 0.5f;
            yield return 1f;
            yield return 2.5f;
        }

        private static IEnumerable<RangeQuery> RangeQueries()
        {
            yield return new RangeQuery(Vector2.zero, 0f);
            yield return new RangeQuery(new Vector2(2f, 2f), 0f);
            yield return new RangeQuery(Vector2.zero, 1f);
            yield return new RangeQuery(Vector2.zero, 2f);
            yield return new RangeQuery(new Vector2(-3f, -3f), 4f);
            yield return new RangeQuery(new Vector2(0.5f, 0.5f), 1.5f);
            yield return new RangeQuery(Vector2.zero, 1000f);
        }

        private static IEnumerable<BoxQuery> BoxQueries()
        {
            yield return new BoxQuery(new Vector2(-2f, -2f), new Vector2(2f, 2f));
            yield return new BoxQuery(Vector2.zero, Vector2.zero);
            yield return new BoxQuery(new Vector2(2f, 2f), new Vector2(2f, 2f));
            yield return new BoxQuery(new Vector2(-8f, -8f), new Vector2(-4f, -4f));
            yield return new BoxQuery(new Vector2(-1024f, -1024f), new Vector2(1024f, 1024f));
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

            internal RangeQuery(Vector2 center, float radius)
            {
                this.center = center;
                this.radius = radius;
            }

            public override string ToString()
            {
                return $"range(center: {center}, radius: {radius})";
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
