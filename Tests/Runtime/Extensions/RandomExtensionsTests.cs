// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
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
        /// Every ranged draw on <see cref="IRandom"/> has a sibling that answers the low bound
        /// where the strict one raises.
        /// </summary>
        /// <remarks>
        /// Collapsing a serialized min/max pair is how a designer asks for "no spread", and these
        /// draws live in coroutines and periodic ticks, where the exception the strict overload
        /// raises ends the loop for the rest of the level (#546). Driven off the shipped surface
        /// rather than a list, so a ranged draw added to <see cref="IRandom"/> later without its
        /// sibling fails here.
        /// <para>
        /// A range is identified by its parameter <b>names</b>, not by its shape:
        /// <c>NextGaussian(double mean, double stdDev)</c> and
        /// <c>NextEnumExcept(T exception1, T exception2)</c> both take two same-typed arguments and
        /// neither is a range. The first version of this test matched on shape and reported both.
        /// </para>
        /// <para>
        /// Scope is <see cref="IRandom"/> itself. The composed ranged draws in
        /// <see cref="RandomExtensions"/> -- <c>NextVector2Int(min, max)</c>,
        /// <c>NextVector3Int(min, max)</c>, <c>NextAngle(min, max)</c> -- throw on a collapsed range
        /// too, and are tracked separately because naming their siblings collides with the existing
        /// <c>NextVector2InRange(range, origin)</c>.
        /// </para>
        /// </remarks>
        [Test]
        public void EveryRangedDrawHasANonThrowingSibling()
        {
            List<string> missing = new();
            int ranged = 0;
            foreach (MethodInfo strict in typeof(IRandom).GetMethods())
            {
                ParameterInfo[] parameters = strict.GetParameters();
                if (
                    parameters.Length != 2
                    || !string.Equals(parameters[0].Name, "min", StringComparison.Ordinal)
                    || !string.Equals(parameters[1].Name, "max", StringComparison.Ordinal)
                    || parameters[0].ParameterType != parameters[1].ParameterType
                    || parameters[0].ParameterType != strict.ReturnType
                )
                {
                    continue;
                }

                ranged++;
                string expected =
                    strict.Name == nameof(IRandom.Next)
                        ? "NextIntInRange"
                        : strict.Name + "InRange";
                MethodInfo sibling = typeof(RandomExtensions).GetMethod(
                    expected,
                    new[] { typeof(IRandom), strict.ReturnType, strict.ReturnType }
                );
                if (sibling == null)
                {
                    missing.Add($"{strict.ReturnType.Name} {strict.Name} -> {expected}");
                }
            }

            Assert.AreEqual(
                8,
                ranged,
                "the sweep must find every ranged draw; a different count means the predicate stopped matching"
            );
            CollectionAssert.IsEmpty(
                missing,
                "a ranged draw without a non-throwing sibling leaves an authored min/max pair able to kill a coroutine"
            );
        }

        /// <summary>
        /// The empty range answers the low bound, for every type, however it is empty.
        /// </summary>
        [Test]
        public void AnEmptyRangeAnswersTheLowBound()
        {
            SystemRandom rng = new(7);

            Assert.AreEqual(3, rng.NextIntInRange(3, 3));
            Assert.AreEqual(5, rng.NextIntInRange(5, 2));
            Assert.AreEqual(-4, rng.NextIntInRange(-4, -4));
            Assert.AreEqual(int.MaxValue, rng.NextIntInRange(int.MaxValue, int.MinValue));

            Assert.AreEqual(3u, rng.NextUintInRange(3u, 3u));
            Assert.AreEqual(5u, rng.NextUintInRange(5u, 2u));
            Assert.AreEqual(uint.MaxValue, rng.NextUintInRange(uint.MaxValue, 0u));

            Assert.AreEqual((short)3, rng.NextShortInRange(3, 3));
            Assert.AreEqual((short)-4, rng.NextShortInRange(-4, -9));
            Assert.AreEqual(short.MaxValue, rng.NextShortInRange(short.MaxValue, short.MinValue));

            Assert.AreEqual((byte)3, rng.NextByteInRange(3, 3));
            Assert.AreEqual((byte)9, rng.NextByteInRange(9, 4));
            Assert.AreEqual(byte.MaxValue, rng.NextByteInRange(byte.MaxValue, 0));

            Assert.AreEqual(3L, rng.NextLongInRange(3L, 3L));
            Assert.AreEqual(5L, rng.NextLongInRange(5L, 2L));
            Assert.AreEqual(long.MaxValue, rng.NextLongInRange(long.MaxValue, long.MinValue));

            Assert.AreEqual(3ul, rng.NextUlongInRange(3ul, 3ul));
            Assert.AreEqual(5ul, rng.NextUlongInRange(5ul, 2ul));
            Assert.AreEqual(ulong.MaxValue, rng.NextUlongInRange(ulong.MaxValue, 0ul));

            Assert.AreEqual(3f, rng.NextFloatInRange(3f, 3f));
            Assert.AreEqual(5f, rng.NextFloatInRange(5f, 2f));
            Assert.AreEqual(0f, rng.NextFloatInRange(0f, 0f));
            Assert.AreEqual(-4f, rng.NextFloatInRange(-4f, -4f));

            Assert.AreEqual(3d, rng.NextDoubleInRange(3d, 3d));
            Assert.AreEqual(5d, rng.NextDoubleInRange(5d, 2d));
            Assert.AreEqual(-4d, rng.NextDoubleInRange(-4d, -4d));
        }

        /// <summary>
        /// A NaN bound makes <c>high &lt;= low</c> false, so the strict overload would answer NaN
        /// rather than raise. Only the floating-point siblings can hit this.
        /// </summary>
        [Test]
        public void ANotANumberBoundAnswersTheLowBound()
        {
            SystemRandom rng = new(7);

            Assert.AreEqual(1f, rng.NextFloatInRange(1f, float.NaN));
            Assert.IsNaN(rng.NextFloatInRange(float.NaN, 5f));
            Assert.AreEqual(1d, rng.NextDoubleInRange(1d, double.NaN));
            Assert.IsNaN(rng.NextDoubleInRange(double.NaN, 5d));
        }

        /// <summary>
        /// A generator that has not been wired up yet degrades to the authored minimum.
        /// </summary>
        [Test]
        public void ANullGeneratorAnswersTheLowBound()
        {
            IRandom absent = null;

            Assert.AreEqual(2, absent.NextIntInRange(2, 9));
            Assert.AreEqual(2u, absent.NextUintInRange(2u, 9u));
            Assert.AreEqual((short)2, absent.NextShortInRange(2, 9));
            Assert.AreEqual((byte)2, absent.NextByteInRange(2, 9));
            Assert.AreEqual(2L, absent.NextLongInRange(2L, 9L));
            Assert.AreEqual(2ul, absent.NextUlongInRange(2ul, 9ul));
            Assert.AreEqual(2f, absent.NextFloatInRange(2f, 9f));
            Assert.AreEqual(2d, absent.NextDoubleInRange(2d, 9d));
        }

        /// <summary>
        /// A range that is not empty still draws from it, so the softened contract costs nothing
        /// where the strict one already worked.
        /// </summary>
        [Test]
        public void ANonEmptyRangeStillDrawsFromIt()
        {
            SystemRandom rng = new(11);

            for (int i = 0; i < 512; ++i)
            {
                Assert.That(rng.NextIntInRange(2, 5), Is.InRange(2, 4));
                Assert.That(rng.NextUintInRange(2u, 5u), Is.InRange(2u, 4u));
                Assert.That(rng.NextShortInRange(2, 5), Is.InRange((short)2, (short)4));
                Assert.That(rng.NextByteInRange(2, 5), Is.InRange((byte)2, (byte)4));
                Assert.That(rng.NextLongInRange(2L, 5L), Is.InRange(2L, 4L));
                Assert.That(rng.NextUlongInRange(2ul, 5ul), Is.InRange(2ul, 4ul));

                float sampled = rng.NextFloatInRange(2f, 5f);
                Assert.GreaterOrEqual(sampled, 2f);
                Assert.Less(sampled, 5f);

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

            Assert.Throws<ArgumentException>(() => rng.Next(3, 3));
            Assert.Throws<ArgumentException>(() => rng.NextUint(3u, 3u));
            Assert.Throws<ArgumentException>(() => rng.NextShort(3, 3));
            Assert.Throws<ArgumentException>(() => rng.NextByte(3, 3));
            Assert.Throws<ArgumentException>(() => rng.NextLong(3L, 3L));
            Assert.Throws<ArgumentException>(() => rng.NextUlong(3ul, 3ul));
            Assert.Throws<ArgumentException>(() => rng.NextFloat(3f, 3f));
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
