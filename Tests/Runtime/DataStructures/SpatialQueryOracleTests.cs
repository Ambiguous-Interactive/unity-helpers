// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;
    using Sample = WallstopStudios.UnityHelpers.Tests.TestUtils.SpatialQueryOracle.Sample;

    /// <summary>
    /// The oracle is the yardstick every other spatial suite is measured against, so it needs its
    /// own. These cases hold it against expectations written out by hand rather than computed, and
    /// they exercise the sort-and-trim with <c>k</c> below <c>n</c> -- where the suites that drive
    /// the real structures with <c>k = n</c> leave it a no-op.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SpatialQueryOracleTests
    {
        [Test]
        public void NearestTrimsToTheRequestedCountInDistanceOrder()
        {
            Sample[] samples = Ladder();

            List<int> picks = SpatialQueryOracle.Nearest2D(samples, Vector2.zero, 3);

            CollectionAssert.AreEqual(new[] { 2, 0, 4 }, picks);
        }

        [Test]
        public void NearestBreaksDistanceTiesByInsertionIndex()
        {
            Sample[] samples =
            {
                new(new Vector3(0f, 2f, 0f), 10, 0),
                new(new Vector3(2f, 0f, 0f), 11, 1),
                new(new Vector3(-2f, 0f, 0f), 12, 2),
                new(new Vector3(0f, -1f, 0f), 13, 3),
            };

            List<int> picks = SpatialQueryOracle.Nearest2D(samples, Vector2.zero, 3);

            CollectionAssert.AreEqual(new[] { 3, 0, 1 }, picks);
        }

        [Test]
        public void NearestReturnsNothingForANonPositiveCount()
        {
            Sample[] samples = Ladder();

            CollectionAssert.IsEmpty(SpatialQueryOracle.Nearest2D(samples, Vector2.zero, 0));
            CollectionAssert.IsEmpty(SpatialQueryOracle.Nearest3D(samples, Vector3.zero, -1));
        }

        [Test]
        public void NearestClampsARequestLargerThanTheCorpus()
        {
            Sample[] samples = Ladder();

            List<int> picks = SpatialQueryOracle.Nearest3D(
                samples,
                Vector3.zero,
                samples.Length + 4
            );

            Assert.AreEqual(samples.Length, picks.Count);
        }

        [Test]
        public void FarthestIsTheTailOfTheSameOrdering()
        {
            Sample[] samples = Ladder();

            List<int> nearest = SpatialQueryOracle.Nearest2D(samples, Vector2.zero, samples.Length);
            List<int> farthest = SpatialQueryOracle.Farthest2D(samples, Vector2.zero, 2);

            CollectionAssert.AreEqual(
                new[] { nearest[nearest.Count - 2], nearest[nearest.Count - 1] },
                farthest
            );
            CollectionAssert.AreEqual(new[] { 3, 1 }, farthest);
        }

        [Test]
        public void InvalidBoxesAndRadiiMatchNothing()
        {
            Sample[] samples = Ladder();

            CollectionAssert.IsEmpty(
                SpatialQueryOracle.InsideBox2D(
                    samples,
                    new Vector2(float.NaN, -10f),
                    new Vector2(10f, 10f)
                )
            );
            CollectionAssert.IsEmpty(
                SpatialQueryOracle.InsideBox3D(
                    samples,
                    new Vector3(-10f, -10f, -10f),
                    new Vector3(10f, float.NaN, 10f)
                )
            );
            CollectionAssert.IsEmpty(
                SpatialQueryOracle.InsideBox2D(
                    samples,
                    new Vector2(10f, 10f),
                    new Vector2(-10f, -10f)
                )
            );
            CollectionAssert.IsEmpty(SpatialQueryOracle.WithinRadius2D(samples, Vector2.zero, -1f));
            CollectionAssert.IsEmpty(
                SpatialQueryOracle.WithinRadius2D(samples, Vector2.zero, float.NaN)
            );
            CollectionAssert.IsEmpty(
                SpatialQueryOracle.WithinRadius3D(
                    samples,
                    new Vector3(float.PositiveInfinity, 0f, 0f),
                    5f
                )
            );
        }

        /// <summary>
        /// Five samples at distances 1, 4, 2, 5, 3 from the origin, deliberately out of insertion
        /// order so a sort that kept insertion order would be caught.
        /// </summary>
        private static Sample[] Ladder()
        {
            return new[]
            {
                new Sample(new Vector3(2f, 0f, 0f), 20, 0),
                new Sample(new Vector3(0f, 5f, 0f), 21, 1),
                new Sample(new Vector3(1f, 0f, 0f), 22, 2),
                new Sample(new Vector3(0f, 4f, 0f), 23, 3),
                new Sample(new Vector3(0f, 3f, 0f), 24, 4),
            };
        }
    }
}
