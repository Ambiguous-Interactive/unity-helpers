// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class RandomExtensionsTests : CommonTestBase
    {
        private static readonly SystemRandom DeterministicRandom = new(1234);

        /// <summary>
        /// The authored-range draws answer the low bound wherever the throwing overload raises.
        /// </summary>
        /// <remarks>
        /// Collapsing a serialized min/max pair is how a designer asks for "no spread", and these
        /// draws live in coroutines and periodic ticks, where the exception the strict overload
        /// raises ends the loop for the rest of the level (#546).
        /// </remarks>
        [TestCase(3f, 3f, TestName = "NextFloatInRangeCollapsed")]
        [TestCase(5f, 2f, TestName = "NextFloatInRangeInverted")]
        [TestCase(0f, 0f, TestName = "NextFloatInRangeZero")]
        [TestCase(-4f, -4f, TestName = "NextFloatInRangeNegative")]
        [TestCase(1f, float.NaN, TestName = "NextFloatInRangeNotANumber")]
        public void NextFloatInRangeAnswersTheLowBoundInsteadOfThrowing(float low, float high)
        {
            SystemRandom rng = new(7);

            Assert.AreEqual(low, rng.NextFloatInRange(low, high));
            Assert.AreEqual(low, ((IRandom)null).NextFloatInRange(low, high));
        }

        [TestCase(3, 3, TestName = "NextIntInRangeCollapsed")]
        [TestCase(5, 2, TestName = "NextIntInRangeInverted")]
        [TestCase(0, 0, TestName = "NextIntInRangeZero")]
        [TestCase(-4, -4, TestName = "NextIntInRangeNegative")]
        [TestCase(int.MaxValue, int.MinValue, TestName = "NextIntInRangeExtremes")]
        public void NextIntInRangeAnswersTheLowBoundInsteadOfThrowing(int low, int high)
        {
            SystemRandom rng = new(7);

            Assert.AreEqual(low, rng.NextIntInRange(low, high));
            Assert.AreEqual(low, ((IRandom)null).NextIntInRange(low, high));
        }

        [TestCase(3d, 3d, TestName = "NextDoubleInRangeCollapsed")]
        [TestCase(5d, 2d, TestName = "NextDoubleInRangeInverted")]
        [TestCase(0d, 0d, TestName = "NextDoubleInRangeZero")]
        [TestCase(1d, double.NaN, TestName = "NextDoubleInRangeNotANumber")]
        public void NextDoubleInRangeAnswersTheLowBoundInsteadOfThrowing(double low, double high)
        {
            SystemRandom rng = new(7);

            Assert.AreEqual(low, rng.NextDoubleInRange(low, high));
            Assert.AreEqual(low, ((IRandom)null).NextDoubleInRange(low, high));
        }

        /// <summary>
        /// A range that is not empty still draws from it, so the softened contract costs nothing
        /// where the strict one already worked.
        /// </summary>
        [Test]
        public void AnInRangeDrawStaysInsideANonEmptyRange()
        {
            SystemRandom rng = new(11);

            for (int i = 0; i < 512; ++i)
            {
                float sampled = rng.NextFloatInRange(2f, 5f);
                Assert.GreaterOrEqual(sampled, 2f);
                Assert.Less(sampled, 5f);

                int integral = rng.NextIntInRange(2, 5);
                Assert.GreaterOrEqual(integral, 2);
                Assert.Less(integral, 5);

                double precise = rng.NextDoubleInRange(2d, 5d);
                Assert.GreaterOrEqual(precise, 2d);
                Assert.Less(precise, 5d);
            }
        }

        /// <summary>
        /// The strict overloads keep raising, because a computed range that inverts is a bug.
        /// </summary>
        [Test]
        public void TheStrictOverloadsStillRefuseAnEmptyRange()
        {
            SystemRandom rng = new(13);

            Assert.Throws<ArgumentException>(() => rng.NextFloat(3f, 3f));
            Assert.Throws<ArgumentException>(() => rng.Next(3, 3));
            Assert.Throws<ArgumentException>(() => rng.NextDouble(3d, 3d));
        }

        /// <summary>
        /// The message names the two values, so a designer's inspector entry reaches the console.
        /// </summary>
        [Test]
        public void TheStrictOverloadNamesBothBoundsInItsMessage()
        {
            SystemRandom rng = new(17);

            ArgumentException raised = Assert.Throws<ArgumentException>(() =>
                rng.NextFloat(7f, 4f)
            );

            StringAssert.Contains("7", raised.Message);
            StringAssert.Contains("4", raised.Message);
        }

        [Test]
        public void NextOfExceptThrowsWhenCollectionEmpty()
        {
            SystemRandom rng = new(42);
            Assert.Throws<ArgumentException>(() => rng.NextOfExcept(Array.Empty<int>(), 1));
        }

        [Test]
        public void NextOfExceptThrowsWhenAllValuesExcluded()
        {
            SystemRandom rng = new(42);
            int[] source = { 1, 2 };
            Assert.Throws<ArgumentException>(() => rng.NextOfExcept(source, 1, 2));
        }

        [Test]
        public void NextOfExceptReturnsValueNotInExceptions()
        {
            SystemRandom rng = new(42);
            int[] source = { 1, 2, 3, 4 };
            int selected = rng.NextOfExcept(source, 2, 2, 3, 3);
            CollectionAssert.DoesNotContain(new[] { 2, 3 }, selected);
        }

        [Test]
        public void NextWeightedIndexThrowsWhenWeightsDoNotSumPositive()
        {
            SystemRandom rng = new(1);
            Assert.Throws<ArgumentException>(() => rng.NextWeightedIndex(new[] { 0f, -1f }));
        }

        [Test]
        public void NextSubsetReturnsDeterministicReservoirSample()
        {
            SystemRandom rng = new(99);
            int[] source = { 10, 11, 12, 13, 14 };

            IEnumerable<int> subset = rng.NextSubset(source, 3);
            int[] result = subset.ToArray();

            Assert.AreEqual(3, result.Length);
            CollectionAssert.AllItemsAreUnique(result);
            CollectionAssert.IsSubsetOf(result, source);

            // Deterministic snapshot ensures algorithm stability for fixed seed.
            CollectionAssert.AreEqual(new[] { 10, 14, 13 }, result);
        }

        [Test]
        public void NextWeightedIndexHandlesExtremeValues()
        {
            SystemRandom rng = new(1);
            float[] weights = { float.MaxValue / 4f, float.MaxValue / 4f, float.MaxValue / 2f };

            Assert.DoesNotThrow(() => rng.NextWeightedIndex(weights));
        }

        [Test]
        public void NextWeightedPrefersHigherWeights()
        {
            SystemRandom rng = new(2);
            (string label, float weight)[] weighted = { ("low", 1f), ("high", 4f) };

            int lowCount = 0;
            int highCount = 0;
            for (int i = 0; i < 1000; ++i)
            {
                string choice = rng.NextWeighted(weighted);
                if (choice == "low")
                {
                    lowCount++;
                }
                else
                {
                    highCount++;
                }
            }

            Assert.Greater(highCount, lowCount, "Higher weights should be selected more often.");
        }

        [Test]
        public void NextSubsetCountZeroReturnsEmpty()
        {
            SystemRandom rng = new(5);
            int[] source = { 1, 2, 3 };
            IEnumerable<int> subset = rng.NextSubset(source, 0);
            CollectionAssert.IsEmpty(subset);
        }

        [Test]
        public void NextSubsetThrowsWhenCountExceedsSource()
        {
            SystemRandom rng = new(5);
            Assert.Throws<ArgumentException>(() => rng.NextSubset(new[] { 1, 2 }, 3));
        }

        [Test]
        public void NextWeightedElementThrowsWhenLengthsMismatch()
        {
            SystemRandom rng = new(1);
            Assert.Throws<ArgumentException>(() =>
                rng.NextWeightedElement(new[] { "a", "b" }, new[] { 0.5f })
            );
        }

        [Test]
        public void NextWeightedIndexHandlesTinyWeights()
        {
            SystemRandom rng = new(1);
            float tiny = float.Epsilon;
            float[] weights = { tiny, tiny, tiny };
            Assert.DoesNotThrow(() => rng.NextWeightedIndex(weights));
        }

        [Test]
        public void NextFloatAroundRespectsVariance()
        {
            SystemRandom rng = new(3);
            float center = 5f;
            float variance = 0f;
            float sample = rng.NextFloatAround(center, variance);
            Assert.AreEqual(center, sample);

            float rangedSample = rng.NextFloatAround(2f, 0.5f);
            Assert.That(rangedSample, Is.InRange(1.5f, 2.5f));
        }

        [Test]
        public void NextIntAroundRespectsVariance()
        {
            SystemRandom rng = new(3);
            int center = 10;
            int variance = 0;
            int sample = rng.NextIntAround(center, variance);
            Assert.AreEqual(center, sample);
        }

        [Test]
        public void NextOfExceptHandlesAllButOneExcluded()
        {
            SystemRandom rng = new(10);
            int[] values = { 1, 2, 3 };
            int result = rng.NextOfExcept(values, 1, 2);
            Assert.AreEqual(3, result);
        }

        [Test]
        public void NextSubsetEqualCountReturnsCopy()
        {
            SystemRandom rng = new(77);
            int[] source = { 1, 2, 3 };
            int[] subset = rng.NextSubset(source, 3).ToArray();
            CollectionAssert.AreEqual(source, subset);
        }

        [Test]
        public void NextWeightedThrowsWhenAllWeightsZero()
        {
            SystemRandom rng = new(1);
            Assert.Throws<ArgumentException>(() => rng.NextWeightedIndex(new[] { 0f, 0f }));
        }

        [Test]
        public void NextSubsetDeferredEnumerationKeepsResults()
        {
            SystemRandom rng = new(5);
            int[] source = { 1, 2, 3, 4, 5 };
            IEnumerable<int> subset = rng.NextSubset(source, 2);

            using IEnumerator<int> enumerator = subset.GetEnumerator();
            Assert.IsTrue(enumerator.MoveNext());
            int first = enumerator.Current;
            Assert.IsTrue(enumerator.MoveNext());
            int second = enumerator.Current;
            CollectionAssert.Contains(source, first);
            CollectionAssert.Contains(source, second);
        }

        [Test]
        public void NextSubsetNegativeCountThrows()
        {
            SystemRandom rng = new(5);
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextSubset(new[] { 1 }, -1));
        }

        [Test]
        public void NextSubsetCountOneReturnsSingleElement()
        {
            SystemRandom rng = new(42);
            int[] subset = rng.NextSubset(new[] { 1, 2, 3 }, 1).ToArray();
            Assert.AreEqual(1, subset.Length);
            CollectionAssert.Contains(new[] { 1, 2, 3 }, subset[0]);
        }

        [Test]
        public void NextVector2NegativeAmplitudeUsesAbsoluteRange()
        {
            SystemRandom rng = new(7);
            Vector2 result = rng.NextVector2(-2f);

            Assert.That(result.x, Is.InRange(-2f, 2f));
            Assert.That(result.y, Is.InRange(-2f, 2f));

            Vector3 vector3 = rng.NextVector3(-3f);
            Assert.That(vector3.x, Is.InRange(-3f, 3f));
            Assert.That(vector3.y, Is.InRange(-3f, 3f));
            Assert.That(vector3.z, Is.InRange(-3f, 3f));
        }

        [Test]
        public void NextVector2IntNegativeAmplitudeUsesAbsoluteRange()
        {
            SystemRandom rng = new(9);
            Vector2Int result = rng.NextVector2Int(-3);

            Assert.GreaterOrEqual(result.x, -3);
            Assert.Less(result.x, 3);
            Assert.GreaterOrEqual(result.y, -3);
            Assert.Less(result.y, 3);
        }

        [Test]
        public void NextVector3IntZeroAmplitudeReturnsZeroVector()
        {
            SystemRandom rng = new(11);
            Assert.AreEqual(Vector3Int.zero, rng.NextVector3Int(0));
        }

        [Test]
        public void NextVector2InRectZeroWidthLocksXAxis()
        {
            SystemRandom rng = new(13);
            Rect rect = new(5f, 2f, 0f, 4f);

            Vector2 result = rng.NextVector2InRect(rect);

            Assert.AreEqual(rect.xMin, result.x);
            Assert.That(result.y, Is.InRange(rect.yMin, rect.yMax));
        }

        [Test]
        public void NextVector2InRectZeroAreaReturnsMinCorner()
        {
            SystemRandom rng = new(15);
            Rect rect = new(-3f, 8f, 0f, 0f);

            Vector2 result = rng.NextVector2InRect(rect);

            Assert.AreEqual(new Vector2(rect.xMin, rect.yMin), result);
        }

        [Test]
        public void NextVector3InBoundsZeroVolumeReturnsCenter()
        {
            SystemRandom rng = new(21);
            Bounds bounds = new(new Vector3(2f, 3f, 4f), Vector3.zero);

            Assert.AreEqual(bounds.center, rng.NextVector3InBounds(bounds));
        }

        [Test]
        public void NextVector3OnSphereHandlesNegativeRadius()
        {
            SystemRandom rng = new(17);
            Vector3 center = new(1.5f, -2f, 0.25f);
            float radius = -5f;

            Vector3 result = rng.NextVector3OnSphere(radius, center);

            Assert.AreEqual(Mathf.Abs(radius), Vector3.Distance(center, result), 1e-3f);
        }

        [Test]
        public void NextVector3OnSphereZeroRadiusReturnsCenter()
        {
            SystemRandom rng = new(19);
            Vector3 center = new(-1f, 0.5f, 3f);

            Assert.AreEqual(center, rng.NextVector3OnSphere(0f, center));
        }

        [Test]
        public void NextVector3InSphereZeroRadiusReturnsCenter()
        {
            SystemRandom rng = new(23);
            Vector3 center = new(2f, -3f, 4f);

            Assert.AreEqual(center, rng.NextVector3InSphere(0f, center));
        }

        [Test]
        public void NextVector2InRangeZeroRangeReturnsOrigin()
        {
            SystemRandom rng = new(25);
            Vector2 origin = new(-1.25f, 3.4f);

            Assert.AreEqual(origin, rng.NextVector2InRange(0f, origin));
        }

        [TestCase(-5f)]
        [TestCase(-0.25f)]
        public void NextVector2InRangeNegativeRangeUsesAbsolute(float inputRange)
        {
            SystemRandom rng = new(27);
            Vector2 origin = new(0.5f, -2.5f);

            Vector2 result = rng.NextVector2InRange(inputRange, origin);

            Assert.LessOrEqual(Vector2.Distance(origin, result), Mathf.Abs(inputRange));
        }

        [Test]
        public void NextVector2InRangeDefaultsToZeroOrigin()
        {
            SystemRandom rng = new(29);
            float range = 3.5f;

            Vector2 result = rng.NextVector2InRange(range);

            Assert.LessOrEqual(result.magnitude, range);
        }

        [Test]
        public void NextVector3InRangeZeroRangeReturnsOrigin()
        {
            SystemRandom rng = new(31);
            Vector3 origin = new(4f, -2f, 7f);

            Assert.AreEqual(origin, rng.NextVector3InRange(0f, origin));
        }

        [TestCase(-10f)]
        [TestCase(-0.5f)]
        public void NextVector3InRangeNegativeRangeUsesAbsolute(float inputRange)
        {
            SystemRandom rng = new(33);
            Vector3 origin = new(-1f, 2f, -3f);

            Vector3 result = rng.NextVector3InRange(inputRange, origin);

            Assert.LessOrEqual(Vector3.Distance(origin, result), Mathf.Abs(inputRange));
        }

        [Test]
        public void NextVector3InRangeDefaultsToZeroOrigin()
        {
            SystemRandom rng = new(35);
            float range = 4f;

            Vector3 result = rng.NextVector3InRange(range);

            Assert.LessOrEqual(result.magnitude, range);
        }
    }
}
