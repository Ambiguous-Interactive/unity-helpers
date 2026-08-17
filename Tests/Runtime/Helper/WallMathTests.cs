// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// ReSharper disable EqualExpressionComparison
// ReSharper disable CompareOfFloatsByEqualityOperator

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class WallMathTests : CommonTestBase
    {
        private const int TestIterations = 10_000;
        private const float Epsilon = 0.0001f;

        // The contract is not "smaller than max", it is "the next representable value below it".
        // Anything looser passes for an implementation that gives away far more range than it must.
        private static readonly double[] BoundedDoubleMaxima =
        {
            1d,
            10d,
            0.1d,
            double.MaxValue,
            double.Epsilon,
            2.2250738585072014E-308,
            -1d,
            -0.1d,
        };

        private static readonly float[] BoundedFloatMaxima =
        {
            1f,
            10f,
            0.1f,
            float.MaxValue,
            float.Epsilon,
            1.17549435E-38f,
            -1f,
            -0.1f,
        };

        [Test]
        public void BoundedDoubleAtMaxReturnsTheNextRepresentableValueDown()
        {
            foreach (double max in BoundedDoubleMaxima)
            {
                long bits = BitConverter.DoubleToInt64Bits(max);
                double expected = BitConverter.Int64BitsToDouble(max > 0d ? bits - 1L : bits + 1L);

                double result = WallMath.BoundedDouble(max, max);

                Assert.AreEqual(
                    expected,
                    result,
                    $"BoundedDouble({max:R}) gave away more range than one representable step."
                );
                Assert.Less(result, max);
            }
        }

        [Test]
        public void BoundedDoubleAtZeroReturnsTheFirstNegativeSubnormal()
        {
            Assert.AreEqual(-double.Epsilon, WallMath.BoundedDouble(0d, 0d));
        }

        [Test]
        public void BoundedFloatAtMaxReturnsTheNextRepresentableValueDown()
        {
            foreach (float max in BoundedFloatMaxima)
            {
                int bits = BitConverter.SingleToInt32Bits(max);
                float expected = BitConverter.Int32BitsToSingle(max > 0f ? bits - 1 : bits + 1);

                float result = WallMath.BoundedFloat(max, max);

                Assert.AreEqual(
                    expected,
                    result,
                    $"BoundedFloat({max:R}) gave away more range than one representable step."
                );
                Assert.Less(result, max);
            }
        }

        [Test]
        public void BoundedFloatAtZeroReturnsTheFirstNegativeSubnormal()
        {
            Assert.AreEqual(-float.Epsilon, WallMath.BoundedFloat(0f, 0f));
        }

        [Test]
        public void BoundedDoubleValueLessThanMax()
        {
            double value = 5.0;
            double max = 10.0;
            double result = WallMath.BoundedDouble(max, value);
            Assert.AreEqual(value, result);
        }

        [Test]
        public void BoundedDoubleValueEqualToMax()
        {
            double max = 10.0;
            double value = max;
            double result = WallMath.BoundedDouble(max, value);
            Assert.Less(result, max);
            Assert.Greater(result, 0);
        }

        [Test]
        public void BoundedDoubleValueGreaterThanMax()
        {
            double value = 15.0;
            double max = 10.0;
            double result = WallMath.BoundedDouble(max, value);
            Assert.Less(result, value);
        }

        [Test]
        public void BoundedDoubleWithZero()
        {
            double value = 0.0;
            double max = 10.0;
            double result = WallMath.BoundedDouble(max, value);
            Assert.AreEqual(0.0, result);
        }

        [Test]
        public void BoundedDoubleWithNegativeValues()
        {
            double value = -5.0;
            double max = -1.0;
            double result = WallMath.BoundedDouble(max, value);
            Assert.AreEqual(value, result);
        }

        [Test]
        public void BoundedDoubleWithMaxValue()
        {
            double value = double.MaxValue;
            double max = double.MaxValue;
            double result = WallMath.BoundedDouble(max, value);
            Assert.Less(result, max);
        }

        [Test]
        public void BoundedDoubleWithMinValue()
        {
            double value = double.MinValue;
            double max = 0.0;
            double result = WallMath.BoundedDouble(max, value);
            Assert.AreEqual(value, result);
        }

        [Test]
        public void BoundedDoubleWithPositiveInfinity()
        {
            double value = double.PositiveInfinity;
            double max = 100.0;
            double result = WallMath.BoundedDouble(max, value);
            Assert.IsFalse(double.IsInfinity(result));
        }

        [Test]
        public void BoundedDoubleWithNegativeInfinity()
        {
            double value = double.NegativeInfinity;
            double max = 0.0;
            double result = WallMath.BoundedDouble(max, value);
            Assert.AreEqual(value, result);
        }

        [Test]
        public void BoundedDoubleWithNaN()
        {
            double value = double.NaN;
            double max = 10.0;
            double result = WallMath.BoundedDouble(max, value);
            Assert.IsTrue(double.IsNaN(result));
        }

        [Test]
        public void BoundedFloatValueLessThanMax()
        {
            float value = 5.0f;
            float max = 10.0f;
            float result = WallMath.BoundedFloat(max, value);
            Assert.AreEqual(value, result);
        }

        [Test]
        public void BoundedFloatValueEqualToMax()
        {
            float max = 10.0f;
            float value = max;
            float result = WallMath.BoundedFloat(max, value);
            Assert.Less(result, max);
            Assert.Greater(result, 0);
        }

        [Test]
        public void BoundedFloatValueGreaterThanMax()
        {
            float value = 15.0f;
            float max = 10.0f;
            float result = WallMath.BoundedFloat(max, value);
            Assert.Less(result, value);
        }

        [Test]
        public void BoundedFloatWithZero()
        {
            float value = 0.0f;
            float max = 10.0f;
            float result = WallMath.BoundedFloat(max, value);
            Assert.AreEqual(0.0f, result);
        }

        [Test]
        public void BoundedFloatWithNegativeValues()
        {
            float value = -5.0f;
            float max = -1.0f;
            float result = WallMath.BoundedFloat(max, value);
            Assert.AreEqual(value, result);
        }

        [Test]
        public void BoundedFloatWithMaxValue()
        {
            float value = float.MaxValue;
            float max = float.MaxValue;
            float result = WallMath.BoundedFloat(max, value);
            Assert.Less(result, max);
        }

        [Test]
        public void BoundedFloatWithMinValue()
        {
            float value = float.MinValue;
            float max = 0.0f;
            float result = WallMath.BoundedFloat(max, value);
            Assert.AreEqual(value, result);
        }

        [Test]
        public void BoundedFloatWithPositiveInfinity()
        {
            float value = float.PositiveInfinity;
            float max = 100.0f;
            float result = WallMath.BoundedFloat(max, value);
            Assert.IsFalse(float.IsInfinity(result));
        }

        [Test]
        public void BoundedFloatWithNegativeInfinity()
        {
            float value = float.NegativeInfinity;
            float max = 0.0f;
            float result = WallMath.BoundedFloat(max, value);
            Assert.AreEqual(value, result);
        }

        [Test]
        public void BoundedFloatWithNaN()
        {
            float value = float.NaN;
            float max = 10.0f;
            float result = WallMath.BoundedFloat(max, value);
            Assert.IsTrue(float.IsNaN(result));
        }

        [Test]
        public void PositiveModIntPositiveValues()
        {
            Assert.AreEqual(1, 1.PositiveMod(10));
            Assert.AreEqual(5, 5.PositiveMod(10));
            Assert.AreEqual(0, 10.PositiveMod(10));
            Assert.AreEqual(1, 11.PositiveMod(10));
        }

        /// <remarks>
        /// The contract is a result in [0, max). Adding max to the remainder before taking it again
        /// overflows once max is above 2^30, and the function then returned a negative number -- the
        /// one thing its name promises it never does.
        /// </remarks>
        [Test]
        public void PositiveModStaysInRangeForLargeMaxima()
        {
            (int Value, int Max)[] cases =
            {
                (1_499_999_999, 1_500_000_000),
                (1_400_000_000, 1_500_000_000),
                (int.MaxValue - 1, int.MaxValue),
                (int.MaxValue, int.MaxValue),
                (int.MinValue, int.MaxValue),
                (-1, 2_000_000_000),
            };

            foreach ((int value, int max) in cases)
            {
                int result = value.PositiveMod(max);
                Assert.That(
                    result,
                    Is.InRange(0, max - 1),
                    $"PositiveMod({value}, {max}) left the range it promises."
                );
            }
        }

        [Test]
        public void PositiveModLongStaysInRangeForLargeMaxima()
        {
            (long Value, long Max)[] cases =
            {
                (long.MaxValue - 1, long.MaxValue),
                (long.MaxValue, long.MaxValue),
                (long.MinValue, long.MaxValue),
                ((1L << 62) - 1L, 1L << 62),
                (-1L, (1L << 62) + 1L),
            };

            foreach ((long value, long max) in cases)
            {
                long result = value.PositiveMod(max);
                Assert.That(
                    result,
                    Is.InRange(0L, max - 1L),
                    $"PositiveMod({value}, {max}) left the range it promises."
                );
            }
        }

        /// <remarks>
        /// A value already inside the range is its own remainder, exactly. Taking it out and back
        /// through the maximum rounded it.
        /// </remarks>
        [Test]
        public void PositiveModLeavesAnInRangeFloatExactlyAsItWas()
        {
            foreach (float value in new[] { 0.21524368f, 0.010507582f, 359.99997f, 1e-30f })
            {
                float max = value < 1f ? 0.5f : 360f;
                Assert.That(value.PositiveMod(max), Is.EqualTo(value));
            }

            foreach (double value in new[] { 2.967071348320225d, 111.57282778600822d })
            {
                Assert.That(value.PositiveMod(360d), Is.EqualTo(value));
            }
        }

        /// <remarks>
        /// A remainder smaller than half an ulp of the maximum rounds onto the maximum when the
        /// maximum is added to it, which is outside the half-open range the method promises.
        /// </remarks>
        [Test]
        public void PositiveModNeverReturnsTheMaximumItself()
        {
            foreach (float max in new[] { 360f, 2f, 0.5f, 1e6f, 16777216f })
            {
                // A quarter of an ulp below zero: too small to survive being added to max.
                float oneUlpBelowMax = BitConverter.Int32BitsToSingle(
                    BitConverter.SingleToInt32Bits(max) - 1
                );
                float value = (oneUlpBelowMax - max) / 4f;

                float result = value.PositiveMod(max);

                Assert.That(result, Is.GreaterThanOrEqualTo(0f), $"max {max}");
                Assert.That(
                    result,
                    Is.LessThan(max),
                    $"PositiveMod({value:R}, {max:R}) returned the maximum itself."
                );
            }
        }

        [Test]
        public void WrappedAddLongWrapsTheRealSumWhenItOverflows()
        {
            Assert.That(long.MaxValue.WrappedAdd(5L, 10L), Is.InRange(0L, 9L));
            Assert.That(long.MinValue.WrappedAdd(-5L, 10L), Is.InRange(0L, 9L));
            Assert.That(
                long.MaxValue.WrappedAdd(long.MaxValue, 1L << 40),
                Is.InRange(0L, (1L << 40) - 1L)
            );
            Assert.That(4L.WrappedAdd(2L, 5L), Is.EqualTo(1L));
            Assert.That(0L.WrappedAdd(-1L, 5L), Is.EqualTo(4L));
            Assert.That(3L.WrappedIncrement(4L), Is.EqualTo(0L));
        }

        [Test]
        public void WrappedAddFloatingPointWrapsIntoRange()
        {
            Assert.AreEqual(10f, 350f.WrappedAdd(20f, 360f), Epsilon);
            Assert.AreEqual(350f, 10f.WrappedAdd(-20f, 360f), Epsilon);
            Assert.AreEqual(0.1d, 0.9d.WrappedAdd(0.2d, 1.0d), 1e-9d);
            Assert.AreEqual(0.9d, 0.1d.WrappedAdd(-0.2d, 1.0d), 1e-9d);

            for (float angle = -720f; angle <= 720f; angle += 7.5f)
            {
                float wrapped = angle.WrappedAdd(13.25f, 360f);
                Assert.That(wrapped, Is.GreaterThanOrEqualTo(0f));
                Assert.That(wrapped, Is.LessThan(360f));
            }
        }

        /// <remarks>
        /// The sum is what wraps, so it has to be computed where it fits: adding the increment first
        /// overflowed and wrapped to a number that is not congruent to the real sum.
        /// </remarks>
        [Test]
        public void WrappedAddWrapsTheRealSumWhenItOverflows()
        {
            Assert.That(int.MaxValue.WrappedAdd(5, 10), Is.EqualTo(2));
            Assert.That(int.MinValue.WrappedAdd(-5, 10), Is.EqualTo(7));
            Assert.That((int.MaxValue - 3).WrappedAdd(10, 7), Is.InRange(0, 6));
        }

        [Test]
        public void PositiveModIntNegativeValues()
        {
            Assert.AreEqual(9, (-1).PositiveMod(10));
            Assert.AreEqual(5, (-5).PositiveMod(10));
            Assert.AreEqual(0, (-10).PositiveMod(10));
            Assert.AreEqual(9, (-11).PositiveMod(10));
        }

        [Test]
        public void PositiveModIntWithZeroValue()
        {
            Assert.AreEqual(0, 0.PositiveMod(10));
        }

        [Test]
        public void PositiveModIntWithLargeValues()
        {
            int result = 1000000.PositiveMod(7);
            Assert.GreaterOrEqual(result, 0);
            Assert.Less(result, 7);
        }

        [Test]
        public void PositiveModLongPositiveValues()
        {
            Assert.AreEqual(1L, 1L.PositiveMod(10L));
            Assert.AreEqual(5L, 5L.PositiveMod(10L));
            Assert.AreEqual(0L, 10L.PositiveMod(10L));
            Assert.AreEqual(1L, 11L.PositiveMod(10L));
        }

        [Test]
        public void PositiveModLongNegativeValues()
        {
            Assert.AreEqual(9L, (-1L).PositiveMod(10L));
            Assert.AreEqual(5L, (-5L).PositiveMod(10L));
            Assert.AreEqual(0L, (-10L).PositiveMod(10L));
            Assert.AreEqual(9L, (-11L).PositiveMod(10L));
        }

        [Test]
        public void PositiveModLongWithZeroValue()
        {
            Assert.AreEqual(0L, 0L.PositiveMod(10L));
        }

        [Test]
        public void PositiveModLongWithLargeValues()
        {
            long result = 1000000000000L.PositiveMod(7L);
            Assert.GreaterOrEqual(result, 0L);
            Assert.Less(result, 7L);
        }

        [Test]
        public void PositiveModFloatPositiveValues()
        {
            Assert.AreEqual(1f, 1f.PositiveMod(10f), Epsilon);
            Assert.AreEqual(5f, 5f.PositiveMod(10f), Epsilon);
            Assert.AreEqual(0f, 10f.PositiveMod(10f), Epsilon);
            Assert.AreEqual(1.5f, 11.5f.PositiveMod(10f), Epsilon);
        }

        [Test]
        public void PositiveModFloatNegativeValues()
        {
            Assert.AreEqual(9f, (-1f).PositiveMod(10f), Epsilon);
            Assert.AreEqual(5f, (-5f).PositiveMod(10f), Epsilon);
            Assert.AreEqual(0f, (-10f).PositiveMod(10f), Epsilon);
            Assert.AreEqual(9.5f, (-0.5f).PositiveMod(10f), Epsilon);
        }

        [Test]
        public void PositiveModFloatWithZeroValue()
        {
            Assert.AreEqual(0f, 0f.PositiveMod(10f), Epsilon);
        }

        [Test]
        public void PositiveModFloatWithDecimalValues()
        {
            float result = 7.3f.PositiveMod(2.5f);
            Assert.GreaterOrEqual(result, 0f);
            Assert.Less(result, 2.5f);
            Assert.AreEqual(2.3f, result, Epsilon);
        }

        [Test]
        public void PositiveModDoublePositiveValues()
        {
            Assert.AreEqual(1.0, 1.0.PositiveMod(10.0), Epsilon);
            Assert.AreEqual(5.0, 5.0.PositiveMod(10.0), Epsilon);
            Assert.AreEqual(0.0, 10.0.PositiveMod(10.0), Epsilon);
            Assert.AreEqual(1.5, 11.5.PositiveMod(10.0), Epsilon);
        }

        [Test]
        public void PositiveModDoubleNegativeValues()
        {
            Assert.AreEqual(9.0, (-1.0).PositiveMod(10.0), Epsilon);
            Assert.AreEqual(5.0, (-5.0).PositiveMod(10.0), Epsilon);
            Assert.AreEqual(0.0, (-10.0).PositiveMod(10.0), Epsilon);
            Assert.AreEqual(9.5, (-0.5).PositiveMod(10.0), Epsilon);
        }

        [Test]
        public void PositiveModDoubleWithZeroValue()
        {
            Assert.AreEqual(0.0, 0.0.PositiveMod(10.0), Epsilon);
        }

        [Test]
        public void PositiveModDoubleWithDecimalValues()
        {
            double result = 7.3.PositiveMod(2.5);
            Assert.GreaterOrEqual(result, 0.0);
            Assert.Less(result, 2.5);
            Assert.AreEqual(2.3, result, Epsilon);
        }

        [Test]
        public void WrappedAddValueWithinBounds()
        {
            int ten = 10;
            int twelve = ten.WrappedAdd(2, 100);
            Assert.AreEqual(10, ten);
            Assert.AreEqual(12, twelve);
        }

        [Test]
        public void WrappedAddValueExceedsBounds()
        {
            int ten = 10;
            int two = ten.WrappedAdd(2, 10);
            Assert.AreEqual(10, ten);
            Assert.AreEqual(2, two);
        }

        [Test]
        public void WrappedAddRefValueWithinBounds()
        {
            int toChangeToTwelve = 10;
            int returned = WallMath.WrappedAdd(ref toChangeToTwelve, 2, 100);
            Assert.AreEqual(12, returned);
            Assert.AreEqual(12, toChangeToTwelve);
        }

        [Test]
        public void WrappedAddRefValueExceedsBounds()
        {
            int toChangeToTwo = 10;
            int returned = WallMath.WrappedAdd(ref toChangeToTwo, 2, 10);
            Assert.AreEqual(2, returned);
            Assert.AreEqual(2, toChangeToTwo);
        }

        [Test]
        public void WrappedAddWithZeroIncrement()
        {
            int five = 5;
            int result = five.WrappedAdd(0, 10);
            Assert.AreEqual(5, result);
        }

        [Test]
        public void WrappedAddWithNegativeIncrement()
        {
            int ten = 10;
            int result = ten.WrappedAdd(-5, 20);
            Assert.AreEqual(5, result);
        }

        [Test]
        public void WrappedAddWithLargeIncrement()
        {
            int five = 5;
            int result = five.WrappedAdd(25, 10);
            Assert.AreEqual(0, result);
        }

        [Test]
        public void WrappedAddExactlyAtBoundary()
        {
            int nine = 9;
            int result = nine.WrappedAdd(1, 10);
            Assert.AreEqual(0, result);
        }

        [Test]
        public void WrappedIncrementValueWithinBounds()
        {
            int ten = 10;
            int eleven = ten.WrappedIncrement(100);
            Assert.AreEqual(11, eleven);
            Assert.AreEqual(10, ten);
        }

        [Test]
        public void WrappedIncrementValueExceedsBounds()
        {
            int ten = 10;
            int one = ten.WrappedIncrement(10);
            Assert.AreEqual(1, one);
            Assert.AreEqual(10, ten);
        }

        [Test]
        public void WrappedIncrementRefValueWithinBounds()
        {
            int toChangeToEleven = 10;
            int returned = WallMath.WrappedIncrement(ref toChangeToEleven, 100);
            Assert.AreEqual(11, returned);
            Assert.AreEqual(11, toChangeToEleven);
        }

        [Test]
        public void WrappedIncrementRefValueExceedsBounds()
        {
            int toChangeToOne = 10;
            int returned = WallMath.WrappedIncrement(ref toChangeToOne, 10);
            Assert.AreEqual(1, returned);
            Assert.AreEqual(1, toChangeToOne);
        }

        [Test]
        public void WrappedIncrementAtZero()
        {
            int zero = 0;
            int result = zero.WrappedIncrement(10);
            Assert.AreEqual(1, result);
        }

        [Test]
        public void WrappedIncrementAtMaxMinusOne()
        {
            int nine = 9;
            int result = nine.WrappedIncrement(10);
            Assert.AreEqual(0, result);
        }

        [Test]
        public void ClampGenericValueWithinRange()
        {
            float value = 0.5f;
            float clamped = value.Clamp(0f, 1f);
            Assert.AreEqual(0.5f, clamped);
            Assert.AreEqual(0.5f, value);
        }

        [Test]
        public void ClampGenericValueBelowMin()
        {
            float value = -1.0f;
            float clamped = value.Clamp(0f, 1f);
            Assert.AreEqual(0f, clamped);
        }

        [Test]
        public void ClampGenericValueAboveMax()
        {
            float value = 1.25f;
            float clamped = value.Clamp(0f, 1f);
            Assert.AreEqual(1f, clamped);
            Assert.AreEqual(1.25f, value);
        }

        [Test]
        public void ClampGenericValueAtMin()
        {
            float value = 0f;
            float clamped = value.Clamp(0f, 1f);
            Assert.AreEqual(0f, clamped);
        }

        [Test]
        public void ClampGenericValueAtMax()
        {
            float value = 1f;
            float clamped = value.Clamp(0f, 1f);
            Assert.AreEqual(1f, clamped);
        }

        [Test]
        public void ClampGenericIntegerValues()
        {
            int value = 50;
            int clamped = value.Clamp(0, 100);
            Assert.AreEqual(50, clamped);

            value = -10;
            clamped = value.Clamp(0, 100);
            Assert.AreEqual(0, clamped);

            value = 150;
            clamped = value.Clamp(0, 100);
            Assert.AreEqual(100, clamped);
        }

        [Test]
        public void ClampGenericDoubleValues()
        {
            double value = 0.5;
            double clamped = value.Clamp(0.0, 1.0);
            Assert.AreEqual(0.5, clamped);
        }

        [Test]
        public void ClampGenericStringValues()
        {
            string value = "banana";
            string clamped = value.Clamp("apple", "cherry");
            Assert.AreEqual("banana", clamped);

            value = "aardvark";
            clamped = value.Clamp("apple", "cherry");
            Assert.AreEqual("apple", clamped);

            value = "zebra";
            clamped = value.Clamp("apple", "cherry");
            Assert.AreEqual("cherry", clamped);
        }

        [Test]
        public void ClampRectPointInsideBounds()
        {
            Rect rect = new(Vector2.one, Vector2.one);
            Vector2 inside = rect.center;
            Vector2 clamped = rect.Clamp(inside);
            Assert.AreEqual(inside, clamped);
        }

        [Test]
        public void ClampRectPointOutsideRight()
        {
            Rect rect = new(Vector2.zero, Vector2.one);
            Vector2 outside = new(5f, 0.5f);
            Vector2 clamped = rect.Clamp(outside);
            Assert.AreEqual(new Vector2(1f, 0.5f), clamped);
        }

        [Test]
        public void ClampRectPointOutsideLeft()
        {
            Rect rect = new(Vector2.zero, Vector2.one);
            Vector2 outside = new(-5f, 0.5f);
            Vector2 clamped = rect.Clamp(outside);
            Assert.AreEqual(new Vector2(0f, 0.5f), clamped);
        }

        [Test]
        public void ClampRectPointOutsideTop()
        {
            Rect rect = new(Vector2.zero, Vector2.one);
            Vector2 outside = new(0.5f, 5f);
            Vector2 clamped = rect.Clamp(outside);
            Assert.AreEqual(new Vector2(0.5f, 1f), clamped);
        }

        [Test]
        public void ClampRectPointOutsideBottom()
        {
            Rect rect = new(Vector2.zero, Vector2.one);
            Vector2 outside = new(0.5f, -5f);
            Vector2 clamped = rect.Clamp(outside);
            Assert.AreEqual(new Vector2(0.5f, 0f), clamped);
        }

        [Test]
        public void ClampRectPointAtCenter()
        {
            Rect rect = new(Vector2.one, Vector2.one);
            Vector2 clamped = rect.Clamp(rect.center);
            Assert.AreEqual(rect.center, clamped);
        }

        [Test]
        public void ClampRectPointAtCorner()
        {
            Rect rect = new(Vector2.zero, Vector2.one);
            Vector2 topRight = new(rect.max.x, rect.max.y);
            Vector2 clamped = rect.Clamp(topRight);
            Assert.AreEqual(topRight, clamped);
        }

        [Test]
        public void ClampRectWithRefParameter()
        {
            Rect rect = new(Vector2.one, Vector2.one);
            Vector2 outside = new(rect.center.x, 100f);
            Vector2 clamped = rect.Clamp(ref outside);
            Assert.AreEqual(clamped, outside);
            Assert.AreEqual(new Vector2(rect.center.x, 2f), clamped);
        }

        [Test]
        public void ClampRectDiagonalOutside()
        {
            Rect rect = new(Vector2.zero, Vector2.one);
            Vector2 outside = new(10f, 10f);
            Vector2 clamped = rect.Clamp(outside);
            Assert.IsTrue(
                rect.Contains(clamped)
                    || Mathf.Approximately(clamped.x, rect.max.x)
                    || Mathf.Approximately(clamped.y, rect.max.y)
            );
        }

        [Test]
        public void ClampRectWithZeroDirectionFromCenter()
        {
            Rect rect = new(Vector2.zero, new Vector2(2f, 2f));
            Vector2 point = rect.center;
            Vector2 clamped = rect.Clamp(point);
            Assert.AreEqual(rect.center, clamped);
        }

        [Test]
        public void ApproximatelyWithinTolerance()
        {
            Assert.IsTrue(0f.Approximately(0f, 0f));
            Assert.IsTrue(0f.Approximately(0.5f, 1f));
            Assert.IsTrue(5f.Approximately(5.5f, 1f));
            Assert.IsTrue(10f.Approximately(10.04f));
        }

        [Test]
        public void ApproximatelyOutsideTolerance()
        {
            Assert.IsFalse(0.001f.Approximately(0f, 0f));
            Assert.IsFalse(100f.Approximately(5f, 2.4f));
            Assert.IsFalse(0f.Approximately(1f, 0.5f));
        }

        [Test]
        public void ApproximatelyDefaultTolerance()
        {
            Assert.IsTrue(0.001f.Approximately(0.0001f));
            Assert.IsTrue(1.0f.Approximately(1.04f));
            Assert.IsFalse(1.0f.Approximately(1.5f));
        }

        [Test]
        public void ApproximatelyWithNegativeValues()
        {
            Assert.IsTrue((-5f).Approximately(-5.04f));
            Assert.IsFalse((-5f).Approximately(-10f));
        }

        [Test]
        public void ApproximatelyWithZeroTolerance()
        {
            Assert.IsTrue(1f.Approximately(1f, 0f));
            Assert.IsFalse(1f.Approximately(1.0001f, 0f));
        }

        [Test]
        public void ApproximatelyWithNegativeTolerance()
        {
            Assert.IsTrue(5f.Approximately(5.5f, -1f));
            Assert.IsFalse(5f.Approximately(10f, -1f));
        }

        [Test]
        public void ApproximatelyWithInfinity()
        {
            Assert.IsFalse(float.PositiveInfinity.Approximately(float.PositiveInfinity, 1f));
            Assert.IsFalse(float.PositiveInfinity.Approximately(100f, 1000f));
            Assert.IsFalse(100f.Approximately(float.PositiveInfinity, 1000f));
            Assert.IsFalse(float.NegativeInfinity.Approximately(float.NegativeInfinity, 1f));
        }

        [Test]
        public void ApproximatelyWithNaN()
        {
            Assert.IsFalse(float.NaN.Approximately(0f, 1f));
            Assert.IsFalse(0f.Approximately(float.NaN, 1f));
        }

        [Test]
        public void ApproximatelyRandomValues()
        {
            for (int i = 0; i < TestIterations; ++i)
            {
                float target = PRNG.Instance.NextFloat(-1_000, 1_000);
                float delta = PRNG.Instance.NextFloat(1f, 10f);

                float insideOffset = delta * PRNG.Instance.NextFloat();
                if (PRNG.Instance.NextBool())
                {
                    insideOffset *= -1;
                }

                float inside = target + insideOffset;
                Assert.IsTrue(
                    target.Approximately(inside, delta),
                    $"Target {target} is not close enough to {inside} with a delta of {delta}."
                );

                float outsideOffset = delta * PRNG.Instance.NextFloat(1.001f, 10f);
                if (PRNG.Instance.NextBool())
                {
                    outsideOffset *= -1;
                }

                float outside = target + outsideOffset;
                Assert.IsFalse(
                    target.Approximately(outside, delta),
                    $"Target {target} was unexpectedly close to {outside} with a delta of {delta}."
                );
            }
        }

        [Test]
        public void ClampRectInsideRandom()
        {
            const float minimumDimension = 0.1f;
            const float boundaryMargin = 0.001f;

            Assert.Less(
                boundaryMargin * 2,
                minimumDimension,
                "Test precondition: boundaryMargin * 2 must be less than minimumDimension to ensure valid sampling range."
            );

            for (int i = 0; i < TestIterations; ++i)
            {
                Vector2 position = PRNG.Instance.NextVector2InRange(10f);

                Vector2 size;
                do
                {
                    size = PRNG.Instance.NextVector2InRange(10f);
                } while (size.x < minimumDimension || size.y < minimumDimension);

                Rect rect = new(position, size);

                float minX = rect.min.x + boundaryMargin;
                float maxX = rect.max.x - boundaryMargin;
                float minY = rect.min.y + boundaryMargin;
                float maxY = rect.max.y - boundaryMargin;

                Vector2 inside = new(
                    PRNG.Instance.NextFloat(minX, maxX),
                    PRNG.Instance.NextFloat(minY, maxY)
                );
                Assert.IsTrue(rect.Contains(inside), $"Rect {rect} does not contain {inside}.");
                Vector2 clamped = rect.Clamp(inside);
                Assert.AreEqual(
                    inside,
                    clamped,
                    $"Iteration {i}: Expected clamped value {clamped} to equal inside value {inside} for rect {rect}."
                );
            }
        }

        [Test]
        public void ClampRectOutsideRandom()
        {
            Rect rect = new(Vector2.one, Vector2.one);
            for (int i = 0; i < TestIterations; ++i)
            {
                Vector2 outside;
                do
                {
                    outside = PRNG.Instance.NextVector2InRange(10f, rect.center);
                } while (rect.Contains(outside));

                Vector2 originalOutside = outside;
                Vector2 clamped = rect.Clamp(ref outside);
                Assert.AreNotEqual(
                    originalOutside,
                    clamped,
                    $"Iteration {i}: Clamped value {clamped} should differ from original outside value {originalOutside} for rect {rect}."
                );
                Assert.AreEqual(
                    clamped,
                    outside,
                    $"Iteration {i}: Clamp should modify the ref parameter to match the returned value. Expected {clamped}, got {outside}."
                );

                const float shrinkScaleToAccountForPrecision = 0.99f;
                Vector2 delta = rect.center - clamped;
                delta *= shrinkScaleToAccountForPrecision;
                clamped = rect.center + delta;
                Assert.IsTrue(
                    rect.Contains(clamped),
                    $"Expected rect {rect} to contain {clamped}, but it did not."
                );
            }
        }

        [Test]
        [TestCase(0f, 0f, TestName = "ClampRectZeroDimensionsBoth")]
        [TestCase(0f, 1f, TestName = "ClampRectZeroDimensionsWidth")]
        [TestCase(1f, 0f, TestName = "ClampRectZeroDimensionsHeight")]
        public void ClampRectZeroDimensions(float width, float height)
        {
            Rect rect = new(Vector2.one, new Vector2(width, height));
            Vector2 outside = new(10f, 10f);
            Vector2 clamped = rect.Clamp(outside);
            Assert.LessOrEqual(
                clamped.x,
                rect.max.x + Epsilon,
                $"Clamped x {clamped.x} should be <= max x {rect.max.x} + epsilon for rect {rect}."
            );
            Assert.LessOrEqual(
                clamped.y,
                rect.max.y + Epsilon,
                $"Clamped y {clamped.y} should be <= max y {rect.max.y} + epsilon for rect {rect}."
            );
            Assert.GreaterOrEqual(
                clamped.x,
                rect.min.x - Epsilon,
                $"Clamped x {clamped.x} should be >= min x {rect.min.x} - epsilon for rect {rect}."
            );
            Assert.GreaterOrEqual(
                clamped.y,
                rect.min.y - Epsilon,
                $"Clamped y {clamped.y} should be >= min y {rect.min.y} - epsilon for rect {rect}."
            );
        }

        [Test]
        [TestCase(0.0001f, 0.0001f, TestName = "ClampRectVerySmallDimensionsTiny")]
        [TestCase(0.001f, 0.001f, TestName = "ClampRectVerySmallDimensionsSmall")]
        [TestCase(0.01f, 0.01f, TestName = "ClampRectVerySmallDimensionsMedium")]
        public void ClampRectVerySmallDimensions(float width, float height)
        {
            Rect rect = new(Vector2.zero, new Vector2(width, height));
            Vector2 outside = new(10f, 10f);
            Vector2 clamped = rect.Clamp(outside);
            Assert.LessOrEqual(
                clamped.x,
                rect.max.x,
                $"Clamped x {clamped.x} should be <= max x {rect.max.x} for rect {rect}."
            );
            Assert.LessOrEqual(
                clamped.y,
                rect.max.y,
                $"Clamped y {clamped.y} should be <= max y {rect.max.y} for rect {rect}."
            );
            Assert.GreaterOrEqual(
                clamped.x,
                rect.min.x,
                $"Clamped x {clamped.x} should be >= min x {rect.min.x} for rect {rect}."
            );
            Assert.GreaterOrEqual(
                clamped.y,
                rect.min.y,
                $"Clamped y {clamped.y} should be >= min y {rect.min.y} for rect {rect}."
            );
        }

        [Test]
        public void ClampRectBoundaryPointsAreClampedCorrectly()
        {
            Rect rect = new(Vector2.zero, new Vector2(2f, 2f));

            Vector2 minCorner = rect.min;
            Vector2 clampedMin = rect.Clamp(minCorner);
            Assert.AreEqual(minCorner, clampedMin, "Min corner should clamp to itself.");

            Vector2 maxCorner = rect.max;
            Vector2 clampedMax = rect.Clamp(maxCorner);
            Assert.AreEqual(maxCorner, clampedMax, "Max corner should clamp to itself.");

            Vector2 topLeft = new(rect.min.x, rect.max.y);
            Vector2 clampedTopLeft = rect.Clamp(topLeft);
            Assert.AreEqual(topLeft, clampedTopLeft, "Top-left corner should clamp to itself.");

            Vector2 bottomRight = new(rect.max.x, rect.min.y);
            Vector2 clampedBottomRight = rect.Clamp(bottomRight);
            Assert.AreEqual(
                bottomRight,
                clampedBottomRight,
                "Bottom-right corner should clamp to itself."
            );
        }

        [Test]
        public void ClampRectEdgeMidpointsAreClampedCorrectly()
        {
            Rect rect = new(Vector2.zero, new Vector2(4f, 4f));

            Vector2 topEdge = new(rect.center.x, rect.max.y);
            Vector2 clampedTop = rect.Clamp(topEdge);
            Assert.AreEqual(topEdge, clampedTop, "Top edge midpoint should clamp to itself.");

            Vector2 bottomEdge = new(rect.center.x, rect.min.y);
            Vector2 clampedBottom = rect.Clamp(bottomEdge);
            Assert.AreEqual(
                bottomEdge,
                clampedBottom,
                "Bottom edge midpoint should clamp to itself."
            );

            Vector2 leftEdge = new(rect.min.x, rect.center.y);
            Vector2 clampedLeft = rect.Clamp(leftEdge);
            Assert.AreEqual(leftEdge, clampedLeft, "Left edge midpoint should clamp to itself.");

            Vector2 rightEdge = new(rect.max.x, rect.center.y);
            Vector2 clampedRight = rect.Clamp(rightEdge);
            Assert.AreEqual(rightEdge, clampedRight, "Right edge midpoint should clamp to itself.");
        }

        [Test]
        [TestCase(-2f, -2f, TestName = "ClampRectNegativeSizeBothNegative")]
        [TestCase(-2f, 2f, TestName = "ClampRectNegativeSizeWidthNegative")]
        [TestCase(2f, -2f, TestName = "ClampRectNegativeSizeHeightNegative")]
        public void ClampRectNegativeDimensions(float width, float height)
        {
            Rect rect = new(new Vector2(5f, 5f), new Vector2(width, height));
            Vector2 outsideFar = new(100f, 100f);
            Vector2 clamped = rect.Clamp(outsideFar);

            float expectedMinX = Mathf.Min(rect.xMin, rect.xMax);
            float expectedMaxX = Mathf.Max(rect.xMin, rect.xMax);
            float expectedMinY = Mathf.Min(rect.yMin, rect.yMax);
            float expectedMaxY = Mathf.Max(rect.yMin, rect.yMax);

            Assert.GreaterOrEqual(
                clamped.x,
                expectedMinX - Epsilon,
                $"Clamped x {clamped.x} should be >= expected min x {expectedMinX} - epsilon for rect {rect}."
            );
            Assert.LessOrEqual(
                clamped.x,
                expectedMaxX + Epsilon,
                $"Clamped x {clamped.x} should be <= expected max x {expectedMaxX} + epsilon for rect {rect}."
            );
            Assert.GreaterOrEqual(
                clamped.y,
                expectedMinY - Epsilon,
                $"Clamped y {clamped.y} should be >= expected min y {expectedMinY} - epsilon for rect {rect}."
            );
            Assert.LessOrEqual(
                clamped.y,
                expectedMaxY + Epsilon,
                $"Clamped y {clamped.y} should be <= expected max y {expectedMaxY} + epsilon for rect {rect}."
            );
        }

        [Test]
        public void ClampRectCenterPointRemainsUnchanged()
        {
            Rect rect = new(Vector2.zero, new Vector2(10f, 10f));
            Vector2 center = rect.center;
            Vector2 clamped = rect.Clamp(center);
            Assert.AreEqual(center, clamped, "Center point should not be modified.");
        }

        [Test]
        public void BoundedDoubleConsistentBehavior()
        {
            for (int i = 0; i < 1000; ++i)
            {
                double max = PRNG.Instance.NextDouble(1.0, 1000.0);
                double value = PRNG.Instance.NextDouble(0.0, 1500.0);
                double result = WallMath.BoundedDouble(max, value);

                if (value < max)
                {
                    Assert.AreEqual(value, result);
                }
                else
                {
                    Assert.Less(result, value);
                }
            }
        }

        [Test]
        public void BoundedFloatConsistentBehavior()
        {
            for (int i = 0; i < 1000; ++i)
            {
                float max = PRNG.Instance.NextFloat(1f, 1000f);
                float value = PRNG.Instance.NextFloat(0f, 1500f);
                float result = WallMath.BoundedFloat(max, value);

                if (value < max)
                {
                    Assert.AreEqual(value, result);
                }
                else
                {
                    Assert.Less(result, value);
                }
            }
        }

        [Test]
        public void PositiveModRandomInt()
        {
            for (int i = 0; i < 1000; ++i)
            {
                int value = PRNG.Instance.Next(-10000, 10000);
                int max = PRNG.Instance.Next(1, 1000);
                int result = value.PositiveMod(max);

                Assert.GreaterOrEqual(result, 0);
                Assert.Less(result, max);
            }
        }

        [Test]
        public void PositiveModRandomLong()
        {
            for (int i = 0; i < 1000; ++i)
            {
                long value = PRNG.Instance.NextLong(-10000, 10000);
                long max = PRNG.Instance.NextLong(1, 1000);
                long result = value.PositiveMod(max);

                Assert.GreaterOrEqual(result, 0L);
                Assert.Less(result, max);
            }
        }

        [Test]
        public void PositiveModRandomFloat()
        {
            for (int i = 0; i < 1000; ++i)
            {
                float value = PRNG.Instance.NextFloat(-10000f, 10000f);
                float max = PRNG.Instance.NextFloat(1f, 1000f);
                float result = value.PositiveMod(max);

                Assert.GreaterOrEqual(result, 0f);
                Assert.Less(result, max);
            }
        }

        [Test]
        public void PositiveModRandomDouble()
        {
            for (int i = 0; i < 1000; ++i)
            {
                double value = PRNG.Instance.NextDouble(-10000.0, 10000.0);
                double max = PRNG.Instance.NextDouble(1.0, 1000.0);
                double result = value.PositiveMod(max);

                Assert.GreaterOrEqual(result, 0.0);
                Assert.Less(result, max);
            }
        }

        [Test]
        public void WrappedAddMultipleWraps()
        {
            int value = 5;
            int result = value.WrappedAdd(105, 10);
            Assert.AreEqual(0, result);
        }

        [Test]
        public void WrappedIncrementMultipleTimes()
        {
            int value = 9;
            for (int i = 0; i < 10; ++i)
            {
                value = value.WrappedIncrement(10);
            }
            Assert.AreEqual(9, value);
        }

        [Test]
        public void ClampGenericWithEqualMinMax()
        {
            float value = 5f;
            float clamped = value.Clamp(10f, 10f);
            Assert.AreEqual(10f, clamped);
        }

        [Test]
        public void ClampRectWithVerySmallRect()
        {
            Rect rect = new(Vector2.zero, new Vector2(0.001f, 0.001f));
            Vector2 outside = new(10f, 10f);
            Vector2 clamped = rect.Clamp(outside);
            Assert.LessOrEqual(clamped.x, rect.max.x);
            Assert.LessOrEqual(clamped.y, rect.max.y);
        }

        [Test]
        public void ClampRectWithNegativeCoordinates()
        {
            Rect rect = new(new Vector2(-10f, -10f), new Vector2(5f, 5f));
            Vector2 outside = new(-20f, -20f);
            Vector2 clamped = rect.Clamp(outside);
            Assert.IsTrue(Mathf.Approximately(clamped.x, rect.min.x) || rect.Contains(clamped));
        }

        [Test]
        public void BoundedDoubleWithVerySmallValues()
        {
            double value = 0.000001;
            double max = 0.00001;
            double result = WallMath.BoundedDouble(max, value);
            Assert.AreEqual(value, result);
        }

        [Test]
        public void BoundedFloatWithVerySmallValues()
        {
            float value = 0.000001f;
            float max = 0.00001f;
            float result = WallMath.BoundedFloat(max, value);
            Assert.AreEqual(value, result);
        }

        [Test]
        public void BoundedDoubleWithMaxAsNaN()
        {
            double value = 5.0;
            double max = double.NaN;
            double result = WallMath.BoundedDouble(max, value);
            Assert.IsTrue(double.IsNaN(result));
        }

        [Test]
        public void BoundedFloatWithMaxAsNaN()
        {
            float value = 5.0f;
            float max = float.NaN;
            float result = WallMath.BoundedFloat(max, value);
            Assert.IsTrue(float.IsNaN(result));
        }

        [Test]
        public void BoundedDoubleWithMaxAsInfinity()
        {
            double value = 100.0;
            double max = double.PositiveInfinity;
            double result = WallMath.BoundedDouble(max, value);
            Assert.AreEqual(value, result);
        }

        [Test]
        public void BoundedFloatWithMaxAsInfinity()
        {
            float value = 100.0f;
            float max = float.PositiveInfinity;
            float result = WallMath.BoundedFloat(max, value);
            Assert.AreEqual(value, result);
        }

        [Test]
        public void PositiveModIntWithOne()
        {
            Assert.AreEqual(0, 5.PositiveMod(1));
            Assert.AreEqual(0, (-5).PositiveMod(1));
        }

        [Test]
        public void PositiveModLongWithOne()
        {
            Assert.AreEqual(0L, 5L.PositiveMod(1L));
            Assert.AreEqual(0L, (-5L).PositiveMod(1L));
        }

        [Test]
        public void PositiveModFloatWithOne()
        {
            // 5.5 mod 1 is 0.5, not 0. A maximum of 1 is the normalized-phase case, and it used to
            // return 0 for every input -- which this file's own documented example contradicted.
            Assert.AreEqual(0.5f, 5.5f.PositiveMod(1f), Epsilon);
            Assert.AreEqual(0.5f, (-5.5f).PositiveMod(1f), Epsilon);
            Assert.AreEqual(0f, 5f.PositiveMod(1f), Epsilon);
        }

        [Test]
        public void PositiveModDoubleWithOne()
        {
            // -5.5 mod 1 is 0.5 under floored modulo, the same as 5.5 mod 1.
            Assert.AreEqual(0.5, 5.5.PositiveMod(1.0), Epsilon);
            Assert.AreEqual(0.5, (-5.5).PositiveMod(1.0), Epsilon);
            Assert.AreEqual(0.0, 5.0.PositiveMod(1.0), Epsilon);
        }

        [Test]
        public void PositiveModIntWithMaxValue()
        {
            int result = int.MaxValue.PositiveMod(100);
            Assert.GreaterOrEqual(result, 0);
            Assert.Less(result, 100);
        }

        [Test]
        public void PositiveModLongWithMaxValue()
        {
            long result = long.MaxValue.PositiveMod(100L);
            Assert.GreaterOrEqual(result, 0L);
            Assert.Less(result, 100L);
        }

        [Test]
        public void PositiveModIntWithMinValue()
        {
            int result = int.MinValue.PositiveMod(100);
            Assert.GreaterOrEqual(result, 0);
            Assert.Less(result, 100);
        }

        [Test]
        public void PositiveModLongWithMinValue()
        {
            long result = long.MinValue.PositiveMod(100L);
            Assert.GreaterOrEqual(result, 0L);
            Assert.Less(result, 100L);
        }

        [Test]
        public void WrappedAddWithNegativeMax()
        {
            int value = 5;
            int result = value.WrappedAdd(3, -10);
            Assert.GreaterOrEqual(result, -10);
            Assert.Less(result, 0);
        }

        [Test]
        public void WrappedAddWithVeryLargeIncrement()
        {
            int value = 5;
            int result = value.WrappedAdd(int.MaxValue - 10, 100);
            Assert.GreaterOrEqual(result, 0);
            Assert.Less(result, 100);
        }

        [Test]
        public void WrappedAddWithVeryLargeNegativeIncrement()
        {
            int value = 50;
            int result = value.WrappedAdd(int.MinValue + 100, 100);
            Assert.GreaterOrEqual(result, 0);
            Assert.Less(result, 100);
        }

        [Test]
        public void WrappedAddNegativeValuePositiveIncrement()
        {
            int value = 5;
            int result = value.WrappedAdd(-10, 20);
            Assert.AreEqual(15, result);
        }

        [Test]
        public void WrappedIncrementFromZero()
        {
            int value = 0;
            int result = WallMath.WrappedIncrement(ref value, 5);
            Assert.AreEqual(1, result);
            Assert.AreEqual(1, value);
        }

        [Test]
        public void WrappedIncrementWrapToZero()
        {
            int value = 4;
            int result = WallMath.WrappedIncrement(ref value, 5);
            Assert.AreEqual(0, result);
            Assert.AreEqual(0, value);
        }

        [Test]
        public void ClampGenericWithReversedMinMax()
        {
            float value = 5f;
            float clamped = value.Clamp(10f, 0f);
            Assert.AreEqual(10f, clamped);
        }

        [Test]
        public void ClampGenericNegativeRange()
        {
            int value = -5;
            int clamped = value.Clamp(-10, -1);
            Assert.AreEqual(-5, clamped);

            value = -15;
            clamped = value.Clamp(-10, -1);
            Assert.AreEqual(-10, clamped);

            value = 0;
            clamped = value.Clamp(-10, -1);
            Assert.AreEqual(-1, clamped);
        }

        [Test]
        public void ClampRectPointOnEdge()
        {
            Rect rect = new(Vector2.zero, Vector2.one);
            Vector2 leftEdge = new(0f, 0.5f);
            Vector2 clamped = rect.Clamp(leftEdge);
            Assert.AreEqual(leftEdge, clamped);

            Vector2 rightEdge = new(1f, 0.5f);
            clamped = rect.Clamp(rightEdge);
            Assert.AreEqual(rightEdge, clamped);

            Vector2 topEdge = new(0.5f, 1f);
            clamped = rect.Clamp(topEdge);
            Assert.AreEqual(topEdge, clamped);

            Vector2 bottomEdge = new(0.5f, 0f);
            clamped = rect.Clamp(bottomEdge);
            Assert.AreEqual(bottomEdge, clamped);
        }

        [Test]
        public void ClampRectAllCorners()
        {
            Rect rect = new(Vector2.zero, new Vector2(10f, 10f));

            Vector2 bottomLeft = rect.min;
            Assert.AreEqual(bottomLeft, rect.Clamp(bottomLeft));

            Vector2 bottomRight = new(rect.max.x, rect.min.y);
            Assert.AreEqual(bottomRight, rect.Clamp(bottomRight));

            Vector2 topLeft = new(rect.min.x, rect.max.y);
            Assert.AreEqual(topLeft, rect.Clamp(topLeft));

            Vector2 topRight = rect.max;
            Assert.AreEqual(topRight, rect.Clamp(topRight));
        }

        [Test]
        public void ClampRectDiagonalAllDirections()
        {
            Rect rect = new(Vector2.zero, Vector2.one);

            Vector2 topRightOutside = new(10f, 10f);
            Vector2 clamped = rect.Clamp(topRightOutside);
            Assert.LessOrEqual(clamped.x, rect.max.x);
            Assert.LessOrEqual(clamped.y, rect.max.y);

            Vector2 topLeftOutside = new(-10f, 10f);
            clamped = rect.Clamp(topLeftOutside);
            Assert.GreaterOrEqual(clamped.x, rect.min.x);
            Assert.LessOrEqual(clamped.y, rect.max.y);

            Vector2 bottomRightOutside = new(10f, -10f);
            clamped = rect.Clamp(bottomRightOutside);
            Assert.LessOrEqual(clamped.x, rect.max.x);
            Assert.GreaterOrEqual(clamped.y, rect.min.y);

            Vector2 bottomLeftOutside = new(-10f, -10f);
            clamped = rect.Clamp(bottomLeftOutside);
            Assert.GreaterOrEqual(clamped.x, rect.min.x);
            Assert.GreaterOrEqual(clamped.y, rect.min.y);
        }

        [Test]
        public void ClampRectWithZeroSize()
        {
            Rect rect = new(Vector2.one, Vector2.zero);
            Vector2 outside = new(10f, 10f);
            Vector2 clamped = rect.Clamp(outside);
            Assert.AreEqual(rect.center, clamped);
        }

        [Test]
        public void ClampRectWithNegativeSize()
        {
            Rect rect = new(Vector2.zero, new Vector2(-5f, -5f));
            Vector2 outside = new(10f, 10f);
            Vector2 clamped = rect.Clamp(outside);
            Assert.LessOrEqual(clamped.x, rect.max.x);
            Assert.LessOrEqual(clamped.y, rect.max.y);
        }

        [Test]
        public void ApproximatelyExactMatch()
        {
            Assert.IsTrue(5f.Approximately(5f, 0f));
            Assert.IsTrue(0f.Approximately(0f, 0f));
            Assert.IsTrue((-5f).Approximately(-5f, 0f));
        }

        [Test]
        public void ApproximatelyExactBoundary()
        {
            Assert.IsTrue(0f.Approximately(1f, 1f));
            Assert.IsTrue(1f.Approximately(0f, 1f));
            Assert.IsFalse(0f.Approximately(1.001f, 1f));
            Assert.IsFalse(1.001f.Approximately(0f, 1f));
        }

        [Test]
        public void ApproximatelyVeryLargeValues()
        {
            Assert.IsTrue(1000000f.Approximately(1000001f, 2f));
            Assert.IsFalse(1000000f.Approximately(1000010f, 2f));
        }

        [Test]
        public void ApproximatelyVerySmallDifferences()
        {
            Assert.IsTrue(0.0001f.Approximately(0.0002f, 0.0001f));
            Assert.IsFalse(0.0001f.Approximately(0.0003f, 0.0001f));
        }

        [Test]
        public void ApproximatelyMixedSigns()
        {
            Assert.IsTrue(1f.Approximately(-1f, 2.5f));
            Assert.IsFalse(1f.Approximately(-1f, 1.5f));
        }

        [Test]
        public void ApproximatelyDoubleWithinTolerance()
        {
            Assert.IsTrue(0.0.Approximately(0.0, 0.0));
            Assert.IsTrue(0.0.Approximately(0.5, 1.0));
            Assert.IsTrue(5.0.Approximately(5.5, 1.0));
            Assert.IsTrue(10.0.Approximately(10.04));
        }

        [Test]
        public void ApproximatelyDoubleOutsideTolerance()
        {
            Assert.IsFalse(0.001.Approximately(0.0, 0.0));
            Assert.IsFalse(100.0.Approximately(5.0, 2.4));
            Assert.IsFalse(0.0.Approximately(1.0, 0.5));
        }

        [Test]
        public void ApproximatelyDoubleDefaultTolerance()
        {
            Assert.IsTrue(0.001.Approximately(0.0001));
            Assert.IsTrue(1.0.Approximately(1.04));
            Assert.IsFalse(1.0.Approximately(1.5));
        }

        [Test]
        public void ApproximatelyDoubleWithNegativeValues()
        {
            Assert.IsTrue((-5.0).Approximately(-5.04));
            Assert.IsFalse((-5.0).Approximately(-10.0));
        }

        [Test]
        public void ApproximatelyDoubleWithInfinity()
        {
            Assert.IsFalse(double.PositiveInfinity.Approximately(double.PositiveInfinity, 1.0));
            Assert.IsFalse(double.PositiveInfinity.Approximately(100.0, 1000.0));
            Assert.IsFalse(100.0.Approximately(double.PositiveInfinity, 1000.0));
            Assert.IsFalse(double.NegativeInfinity.Approximately(double.NegativeInfinity, 1.0));
        }

        [Test]
        public void ApproximatelyDoubleWithNaN()
        {
            Assert.IsFalse(double.NaN.Approximately(0.0, 1.0));
            Assert.IsFalse(0.0.Approximately(double.NaN, 1.0));
        }

        [Test]
        public void Vector2ApproximatelyMagnitudeWithinTolerance()
        {
            Vector2 lhs = Vector2.zero;
            Vector2 rhs = new(0.0005f, -0.0004f);
            Assert.IsTrue(lhs.Approximately(rhs));
        }

        [Test]
        public void Vector2ApproximatelyMagnitudeOutsideTolerance()
        {
            Vector2 lhs = Vector2.zero;
            Vector2 rhs = new(0.005f, 0f);
            Assert.IsFalse(lhs.Approximately(rhs));
        }

        [Test]
        public void Vector2ApproximatelyComponentMode()
        {
            Vector2 lhs = new(1f, 2f);
            Vector2 rhs = new(1.0005f, 2.0005f);
            Assert.IsTrue(
                lhs.Approximately(
                    rhs,
                    tolerance: 0.001f,
                    mode: WallMath.VectorApproximationMode.Components
                )
            );

            Vector2 far = new(1.01f, 2f);
            Assert.IsFalse(
                lhs.Approximately(
                    far,
                    tolerance: 0.001f,
                    mode: WallMath.VectorApproximationMode.Components
                )
            );
        }

        [Test]
        public void Vector2ApproximatelyHonorsDelta()
        {
            Vector2 lhs = Vector2.zero;
            Vector2 rhs = new(0f, 0.002f);

            Assert.IsFalse(lhs.Approximately(rhs, tolerance: 0.001f));
            Assert.IsTrue(lhs.Approximately(rhs, tolerance: 0.001f, delta: 0.0011f));
        }

        [Test]
        public void Vector2ApproximatelyNonFiniteIsFalse()
        {
            Vector2 lhs = new(float.NaN, 0f);
            Assert.IsFalse(lhs.Approximately(Vector2.zero));
        }

        [Test]
        public void Vector3ApproximatelyMagnitudeWithinTolerance()
        {
            Vector3 lhs = Vector3.zero;
            Vector3 rhs = new(0.0005f, -0.0004f, 0.0002f);
            Assert.IsTrue(lhs.Approximately(rhs));
        }

        [Test]
        public void Vector3ApproximatelyComponentMode()
        {
            Vector3 lhs = new(1f, 2f, 3f);
            Vector3 rhs = new(1.0005f, 2.0005f, 2.9995f);
            Assert.IsTrue(
                lhs.Approximately(
                    rhs,
                    tolerance: 0.001f,
                    mode: WallMath.VectorApproximationMode.Components
                )
            );

            Vector3 far = new(1f, 2.01f, 3f);
            Assert.IsFalse(
                lhs.Approximately(
                    far,
                    tolerance: 0.001f,
                    mode: WallMath.VectorApproximationMode.Components
                )
            );
        }

        [Test]
        public void Vector3ApproximatelyNonFiniteIsFalse()
        {
            Vector3 lhs = new(float.PositiveInfinity, 0f, 0f);
            Assert.IsFalse(lhs.Approximately(Vector3.zero));
        }

        [Test]
        public void ColorApproximatelyWithinTolerance()
        {
            Color lhs = new(0.5f, 0.4f, 0.25f, 0.75f);
            Color rhs = new(0.502f, 0.398f, 0.249f, 0.749f);
            Assert.IsTrue(lhs.Approximately(rhs, tolerance: 0.01f));
        }

        [Test]
        public void ColorApproximatelyHonorsDelta()
        {
            Color lhs = Color.white;
            Color rhs = new(0.95f, 0.95f, 0.95f, 0.95f);

            Assert.IsFalse(lhs.Approximately(rhs, tolerance: 0.02f));
            Assert.IsTrue(lhs.Approximately(rhs, tolerance: 0.02f, delta: 0.03f));
        }

        [Test]
        public void ColorApproximatelyCanIgnoreAlpha()
        {
            Color lhs = new(0.1f, 0.2f, 0.3f, 0f);
            Color rhs = new(0.1f, 0.2f, 0.3f, 1f);

            Assert.IsFalse(lhs.Approximately(rhs));
            Assert.IsTrue(lhs.Approximately(rhs, includeAlpha: false));
        }

        [Test]
        public void ColorApproximatelyNonFiniteIsFalse()
        {
            Color lhs = new(float.NaN, 0f, 0f, 1f);
            Assert.IsFalse(lhs.Approximately(Color.black));
        }

        [Test]
        public void Color32ApproximatelyUsesByteTolerance()
        {
            Color32 lhs = new(10, 20, 30, 40);
            Color32 rhs = new(12, 18, 31, 39);

            Assert.IsFalse(lhs.Approximately(rhs, tolerance: 1));
            Assert.IsTrue(lhs.Approximately(rhs, tolerance: 3));
        }

        [Test]
        public void BoundedDoubleWithSameValue()
        {
            for (int i = 0; i < 100; ++i)
            {
                double value = PRNG.Instance.NextDouble(-1000.0, 1000.0);
                double result = WallMath.BoundedDouble(value, value);
                Assert.Less(result, value);
            }
        }

        [Test]
        public void BoundedFloatWithSameValue()
        {
            for (int i = 0; i < 100; ++i)
            {
                float value = PRNG.Instance.NextFloat(-1000f, 1000f);
                float result = WallMath.BoundedFloat(value, value);
                Assert.Less(result, value);
            }
        }

        [Test]
        public void WrappedAddRefConsistentWithNonRef()
        {
            for (int i = 0; i < 100; ++i)
            {
                int value = PRNG.Instance.Next(0, 100);
                int increment = PRNG.Instance.Next(-200, 200);
                int max = PRNG.Instance.Next(10, 50);

                int nonRefResult = value.WrappedAdd(increment, max);

                int refValue = value;
                int refResult = WallMath.WrappedAdd(ref refValue, increment, max);

                Assert.AreEqual(nonRefResult, refResult);
                Assert.AreEqual(refResult, refValue);
            }
        }

        [Test]
        public void WrappedIncrementRefConsistentWithNonRef()
        {
            for (int i = 0; i < 100; ++i)
            {
                int value = PRNG.Instance.Next(0, 100);
                int max = PRNG.Instance.Next(10, 50);

                int nonRefResult = value.WrappedIncrement(max);

                int refValue = value;
                int refResult = WallMath.WrappedIncrement(ref refValue, max);

                Assert.AreEqual(nonRefResult, refResult);
                Assert.AreEqual(refResult, refValue);
            }
        }

        [Test]
        public void ClampGenericWithMultipleTypes()
        {
            byte byteVal = 200;
            Assert.AreEqual((byte)200, byteVal.Clamp((byte)0, (byte)255));
            Assert.AreEqual((byte)100, byteVal.Clamp((byte)0, (byte)100));

            long longVal = 1000000000000L;
            Assert.AreEqual(1000000000000L, longVal.Clamp(0L, long.MaxValue));
            Assert.AreEqual(100L, longVal.Clamp(0L, 100L));

            decimal decimalVal = 5.5m;
            Assert.AreEqual(5.5m, decimalVal.Clamp(0m, 10m));
            Assert.AreEqual(10m, decimalVal.Clamp(10m, 20m));
        }

        [Test]
        public void ClampRectRefModifiesParameter()
        {
            Rect rect = new(Vector2.zero, Vector2.one);
            Vector2 outside = new(10f, 10f);
            Vector2 original = outside;

            Vector2 result = rect.Clamp(ref outside);

            Assert.AreNotEqual(original, outside);
            Assert.AreEqual(result, outside);
            Assert.LessOrEqual(outside.x, rect.max.x);
            Assert.LessOrEqual(outside.y, rect.max.y);
        }

        [Test]
        public void ClampRectNonRefDoesNotModifyParameter()
        {
            Rect rect = new(Vector2.zero, Vector2.one);
            Vector2 outside = new(10f, 10f);
            Vector2 original = outside;

            Vector2 result = rect.Clamp(outside);

            Assert.AreEqual(original, outside);
            Assert.AreNotEqual(result, outside);
        }

        [Test]
        public void PositiveModSymmetryCheck()
        {
            for (int i = 1; i < 50; ++i)
            {
                int positiveResult = i.PositiveMod(10);
                int negativeResult = (-i).PositiveMod(10);

                Assert.GreaterOrEqual(positiveResult, 0);
                Assert.Less(positiveResult, 10);
                Assert.GreaterOrEqual(negativeResult, 0);
                Assert.Less(negativeResult, 10);

                Assert.AreEqual((positiveResult + negativeResult) % 10, 0);
            }
        }

        [Test]
        public void WrappedAddCommutativeWithPositiveMod()
        {
            for (int i = 0; i < 100; ++i)
            {
                int value = PRNG.Instance.Next(-1000, 1000);
                int increment = PRNG.Instance.Next(-1000, 1000);
                int max = PRNG.Instance.Next(10, 100);

                int wrappedResult = value.WrappedAdd(increment, max);
                int manualResult = (value + increment).PositiveMod(max);

                Assert.AreEqual(manualResult, wrappedResult);
            }
        }

        [Test]
        public void TotalEqualsFloatNaNBothNaN()
        {
            Assert.IsTrue(float.NaN.TotalEquals(float.NaN));
        }

        [Test]
        public void TotalEqualsFloatNaNOneNaN()
        {
            Assert.IsFalse(float.NaN.TotalEquals(0f));
            Assert.IsFalse(0f.TotalEquals(float.NaN));
            Assert.IsFalse(float.NaN.TotalEquals(float.PositiveInfinity));
            Assert.IsFalse(float.PositiveInfinity.TotalEquals(float.NaN));
        }

        [Test]
        public void TotalEqualsFloatPositiveInfinityBoth()
        {
            Assert.IsTrue(float.PositiveInfinity.TotalEquals(float.PositiveInfinity));
        }

        [Test]
        public void TotalEqualsFloatNegativeInfinityBoth()
        {
            Assert.IsTrue(float.NegativeInfinity.TotalEquals(float.NegativeInfinity));
        }

        [Test]
        public void TotalEqualsFloatInfinityMismatch()
        {
            Assert.IsFalse(float.PositiveInfinity.TotalEquals(float.NegativeInfinity));
            Assert.IsFalse(float.NegativeInfinity.TotalEquals(float.PositiveInfinity));
            Assert.IsFalse(float.PositiveInfinity.TotalEquals(0f));
            Assert.IsFalse(0f.TotalEquals(float.PositiveInfinity));
            Assert.IsFalse(float.NegativeInfinity.TotalEquals(0f));
            Assert.IsFalse(0f.TotalEquals(float.NegativeInfinity));
        }

        [Test]
        public void TotalEqualsFloatRegularValues()
        {
            Assert.IsTrue(0f.TotalEquals(0f));
            Assert.IsTrue(1.5f.TotalEquals(1.5f));
            Assert.IsTrue((-10.25f).TotalEquals(-10.25f));
            Assert.IsTrue(float.MaxValue.TotalEquals(float.MaxValue));
            Assert.IsTrue(float.MinValue.TotalEquals(float.MinValue));
            Assert.IsTrue(float.Epsilon.TotalEquals(float.Epsilon));
        }

        [Test]
        public void TotalEqualsFloatRegularValuesMismatch()
        {
            Assert.IsFalse(0f.TotalEquals(1f));
            Assert.IsFalse(1f.TotalEquals(0f));
            Assert.IsFalse(1.5f.TotalEquals(1.50001f));
            Assert.IsFalse((-5f).TotalEquals(5f));
            Assert.IsFalse(float.MaxValue.TotalEquals(float.MinValue));
        }

        [Test]
        public void TotalEqualsFloatZeroPositiveAndNegative()
        {
            Assert.IsTrue(0f.TotalEquals(-0f));
            Assert.IsTrue((-0f).TotalEquals(0f));
        }

        [Test]
        public void TotalEqualsDoubleNaNBothNaN()
        {
            Assert.IsTrue(double.NaN.TotalEquals(double.NaN));
        }

        [Test]
        public void TotalEqualsDoubleNaNOneNaN()
        {
            Assert.IsFalse(double.NaN.TotalEquals(0.0));
            Assert.IsFalse(0.0.TotalEquals(double.NaN));
            Assert.IsFalse(double.NaN.TotalEquals(double.PositiveInfinity));
            Assert.IsFalse(double.PositiveInfinity.TotalEquals(double.NaN));
        }

        [Test]
        public void TotalEqualsDoublePositiveInfinityBoth()
        {
            Assert.IsTrue(double.PositiveInfinity.TotalEquals(double.PositiveInfinity));
        }

        [Test]
        public void TotalEqualsDoubleNegativeInfinityBoth()
        {
            Assert.IsTrue(double.NegativeInfinity.TotalEquals(double.NegativeInfinity));
        }

        [Test]
        public void TotalEqualsDoubleInfinityMismatch()
        {
            Assert.IsFalse(double.PositiveInfinity.TotalEquals(double.NegativeInfinity));
            Assert.IsFalse(double.NegativeInfinity.TotalEquals(double.PositiveInfinity));
            Assert.IsFalse(double.PositiveInfinity.TotalEquals(0.0));
            Assert.IsFalse(0.0.TotalEquals(double.PositiveInfinity));
            Assert.IsFalse(double.NegativeInfinity.TotalEquals(0.0));
            Assert.IsFalse(0.0.TotalEquals(double.NegativeInfinity));
        }

        [Test]
        public void TotalEqualsDoubleRegularValues()
        {
            Assert.IsTrue(0.0.TotalEquals(0.0));
            Assert.IsTrue(1.5.TotalEquals(1.5));
            Assert.IsTrue((-10.25).TotalEquals(-10.25));
            Assert.IsTrue(double.MaxValue.TotalEquals(double.MaxValue));
            Assert.IsTrue(double.MinValue.TotalEquals(double.MinValue));
            Assert.IsTrue(double.Epsilon.TotalEquals(double.Epsilon));
        }

        [Test]
        public void TotalEqualsDoubleRegularValuesMismatch()
        {
            Assert.IsFalse(0.0.TotalEquals(1.0));
            Assert.IsFalse(1.0.TotalEquals(0.0));
            Assert.IsFalse(1.5.TotalEquals(1.50001));
            Assert.IsFalse((-5.0).TotalEquals(5.0));
            Assert.IsFalse(double.MaxValue.TotalEquals(double.MinValue));
        }

        [Test]
        public void TotalEqualsDoubleZeroPositiveAndNegative()
        {
            Assert.IsTrue(0.0.TotalEquals(-0.0));
            Assert.IsTrue((-0.0).TotalEquals(0.0));
        }

        [Test]
        public void TotalEqualsFloatVeryCloseValues()
        {
            float value = 1.234567f;
            float nextValue = BitConverter.Int32BitsToSingle(
                BitConverter.SingleToInt32Bits(value) + 1
            );
            Assert.IsFalse(value.TotalEquals(nextValue));
        }

        [Test]
        public void TotalEqualsDoubleVeryCloseValues()
        {
            double value = 1.234567890123456;
            double nextValue = BitConverter.Int64BitsToDouble(
                BitConverter.DoubleToInt64Bits(value) + 1
            );
            Assert.IsFalse(value.TotalEquals(nextValue));
        }

        [Test]
        public void TotalEqualsFloatSymmetric()
        {
            float[] values =
            {
                0f,
                1f,
                -1f,
                float.MaxValue,
                float.MinValue,
                float.Epsilon,
                float.NaN,
                float.PositiveInfinity,
                float.NegativeInfinity,
            };

            foreach (float a in values)
            {
                foreach (float b in values)
                {
                    Assert.AreEqual(
                        a.TotalEquals(b),
                        b.TotalEquals(a),
                        $"TotalEquals should be symmetric for {a} and {b}"
                    );
                }
            }
        }

        [Test]
        public void TotalEqualsDoubleSymmetric()
        {
            double[] values =
            {
                0.0,
                1.0,
                -1.0,
                double.MaxValue,
                double.MinValue,
                double.Epsilon,
                double.NaN,
                double.PositiveInfinity,
                double.NegativeInfinity,
            };

            foreach (double a in values)
            {
                foreach (double b in values)
                {
                    Assert.AreEqual(
                        a.TotalEquals(b),
                        b.TotalEquals(a),
                        $"TotalEquals should be symmetric for {a} and {b}"
                    );
                }
            }
        }

        [Test]
        public void TotalEqualsFloatTransitive()
        {
            float a = 5.5f;
            float b = 5.5f;
            float c = 5.5f;

            Assert.IsTrue(a.TotalEquals(b));
            Assert.IsTrue(b.TotalEquals(c));
            Assert.IsTrue(a.TotalEquals(c));

            float nan1 = float.NaN;
            float nan2 = float.NaN;
            float nan3 = float.NaN;

            Assert.IsTrue(nan1.TotalEquals(nan2));
            Assert.IsTrue(nan2.TotalEquals(nan3));
            Assert.IsTrue(nan1.TotalEquals(nan3));
        }

        [Test]
        public void TotalEqualsDoubleTransitive()
        {
            double a = 5.5;
            double b = 5.5;
            double c = 5.5;

            Assert.IsTrue(a.TotalEquals(b));
            Assert.IsTrue(b.TotalEquals(c));
            Assert.IsTrue(a.TotalEquals(c));

            double nan1 = double.NaN;
            double nan2 = double.NaN;
            double nan3 = double.NaN;

            Assert.IsTrue(nan1.TotalEquals(nan2));
            Assert.IsTrue(nan2.TotalEquals(nan3));
            Assert.IsTrue(nan1.TotalEquals(nan3));
        }

        [Test]
        public void TotalEqualsFloatReflexive()
        {
            float[] values =
            {
                0f,
                1f,
                -1f,
                float.MaxValue,
                float.MinValue,
                float.Epsilon,
                float.NaN,
                float.PositiveInfinity,
                float.NegativeInfinity,
            };

            foreach (float value in values)
            {
                Assert.IsTrue(value.TotalEquals(value), $"{value} should equal itself");
            }
        }

        [Test]
        public void TotalEqualsDoubleReflexive()
        {
            double[] values =
            {
                0.0,
                1.0,
                -1.0,
                double.MaxValue,
                double.MinValue,
                double.Epsilon,
                double.NaN,
                double.PositiveInfinity,
                double.NegativeInfinity,
            };

            foreach (double value in values)
            {
                Assert.IsTrue(value.TotalEquals(value), $"{value} should equal itself");
            }
        }

        [Test]
        public void TotalEqualsFloatDifferentFromStandardEquals()
        {
            Assert.IsTrue(float.NaN.TotalEquals(float.NaN));
        }

        [Test]
        public void TotalEqualsDoubleDifferentFromStandardEquals()
        {
            Assert.IsTrue(double.NaN.TotalEquals(double.NaN));
        }

        [Test]
        public void TotalEqualsFloatRandomNormalValues()
        {
            for (int i = 0; i < TestIterations; ++i)
            {
                float value = PRNG.Instance.NextFloat(-1000f, 1000f);
                Assert.IsTrue(value.TotalEquals(value));

                float different = value + PRNG.Instance.NextFloat(0.001f, 10f);
                Assert.IsFalse(value.TotalEquals(different));
            }
        }

        [Test]
        public void TotalEqualsDoubleRandomNormalValues()
        {
            for (int i = 0; i < TestIterations; ++i)
            {
                double value = PRNG.Instance.NextDouble(-1000.0, 1000.0);
                Assert.IsTrue(value.TotalEquals(value));

                double different = value + PRNG.Instance.NextDouble(0.001, 10.0);
                Assert.IsFalse(value.TotalEquals(different));
            }
        }
    }
}
