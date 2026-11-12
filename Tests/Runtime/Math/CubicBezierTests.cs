// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Math
{
    using System;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Math;
    using Math = System.Math;

    [TestFixture]
    public sealed class CubicBezierTests
    {
        private static double Interpolate(double start, double end, double parameter)
        {
            return start * (1d - parameter) + end * parameter;
        }

        private static double OracleCoordinate(double first, double second, double parameter)
        {
            double left = Interpolate(0d, first, parameter);
            double middle = Interpolate(first, second, parameter);
            double right = Interpolate(second, 1d, parameter);
            return Interpolate(
                Interpolate(left, middle, parameter),
                Interpolate(middle, right, parameter),
                parameter
            );
        }

        private static double Oracle(CubicBezier easing, double x)
        {
            double low = 0d;
            double high = 1d;
            for (int iteration = 0; iteration < 120; ++iteration)
            {
                double parameter = (low + high) * 0.5d;
                double observed = OracleCoordinate(
                    easing.FirstControlX,
                    easing.SecondControlX,
                    parameter
                );
                if (observed == x)
                {
                    return OracleCoordinate(easing.FirstControlY, easing.SecondControlY, parameter);
                }
                if (observed < x)
                {
                    low = parameter;
                }
                else
                {
                    high = parameter;
                }
            }
            return OracleCoordinate(
                easing.FirstControlY,
                easing.SecondControlY,
                (low + high) * 0.5d
            );
        }

        [TestCase(0.25f, 0.1f, 0.25f, 1f)]
        [TestCase(0.42f, 0f, 1f, 1f)]
        [TestCase(0f, 0f, 0.58f, 1f)]
        [TestCase(0.42f, 0f, 0.58f, 1f)]
        [TestCase(0f, 1f, 0f, -2f)]
        [TestCase(1f, 2f, 1f, -1f)]
        [TestCase(1f, -1f, 0f, 2f)]
        [TestCase(0.1f, -100f, 0.9f, 100f)]
        public void EvaluationMatchesIndependentSubdivision(
            float firstX,
            float firstY,
            float secondX,
            float secondY
        )
        {
            CubicBezier easing = new(firstX, firstY, secondX, secondY);
            Assert.IsTrue(easing.IsValid);
            Assert.AreEqual(0d, easing.Evaluate(0d));
            Assert.AreEqual(1d, easing.Evaluate(1d));
            for (int sample = 1; sample < 100; ++sample)
            {
                double x = sample / 100d;
                Assert.AreEqual(
                    Oracle(easing, x),
                    easing.Evaluate(x),
                    2e-9 * Math.Max(1d, Math.Max(Math.Abs(firstY), Math.Abs(secondY))),
                    $"x={x}"
                );
            }
        }

        [TestCase(0.1d)]
        [TestCase(0.499999999999d)]
        [TestCase(0.4999999999999999d)]
        [TestCase(0.5d)]
        [TestCase(0.5000000000000001d)]
        [TestCase(0.500000000001d)]
        [TestCase(0.9d)]
        public void FlatInputDerivativeMatchesTheClosedForm(double x)
        {
            double offset = x - 0.5d;
            double distance = Math.Pow(Math.Abs(offset) / 4d, 1d / 3d);
            double delta = offset < 0d ? -distance : distance;
            double expected = 0.5d + 3d * delta - 8d * delta * delta * delta;
            Assert.AreEqual(expected, new CubicBezier(1f, -1f, 0f, 2f).Evaluate(x), 2e-14);
        }

        [Test]
        public void SubnormalProgressDoesNotBecomeALargeOutput()
        {
            double result = new CubicBezier(0f, float.MaxValue, 1f, 0f).Evaluate(double.Epsilon);
            Assert.That(result, Is.GreaterThan(0d));
            Assert.That(result, Is.LessThan(1e-100));
        }

        [TestCase(0.9999999999999999d)]
        [TestCase(0.999999999d)]
        public void EndpointWithZeroDerivativeKeepsInputPrecision(double x)
        {
            double parameter = 1d - Math.Pow(1d - x, 1d / 3d);
            double expected = parameter * parameter * parameter;
            Assert.AreEqual(expected, new CubicBezier(1f, 0f, 1f, 0f).Evaluate(x), 2e-15);
        }

        [TestCase(0f, 0f)]
        [TestCase(0.5f, 0.5f)]
        [TestCase(1f, 0f)]
        [TestCase(1f, 1f)]
        public void DiagonalControlsReturnExactInput(float first, float second)
        {
            CubicBezier easing = new(first, first, second, second);
            foreach (double x in new[] { double.Epsilon, 1e-15, 0.1, 0.5, 0.9, 1d - 1e-15 })
            {
                Assert.AreEqual(x, easing.Evaluate(x));
            }
        }

        [TestCase(0f, 0f, 0f, 1f, -2d, 0d)]
        [TestCase(0f, 1f, 0.5f, 2f, -0.5d, -2d)]
        [TestCase(0.25f, 0.5f, 0.75f, 0.5f, -0.5d, -1d)]
        [TestCase(0f, 0f, 1f, 1f, 2d, 2d)]
        [TestCase(1f, 0f, 1f, 1f, 2d, 1d)]
        [TestCase(0.5f, -1f, 1f, 10f, 2d, 5d)]
        public void OutsideInputsFollowCssEndpointLines(
            float firstX,
            float firstY,
            float secondX,
            float secondY,
            double x,
            double expected
        )
        {
            Assert.AreEqual(
                expected,
                new CubicBezier(firstX, firstY, secondX, secondY).Evaluate(x)
            );
        }

        [TestCase(float.NaN, 0f, 1f, 1f)]
        [TestCase(0f, float.PositiveInfinity, 1f, 1f)]
        [TestCase(0f, 0f, float.NegativeInfinity, 1f)]
        [TestCase(0f, 0f, 1f, float.NaN)]
        [TestCase(-0.1f, 0f, 1f, 1f)]
        [TestCase(0f, 0f, 1.1f, 1f)]
        public void InvalidControlsFailWithoutThrowing(
            float firstX,
            float firstY,
            float secondX,
            float secondY
        )
        {
            CubicBezier easing = new(firstX, firstY, secondX, secondY);
            Assert.IsFalse(easing.IsValid);
            Assert.IsTrue(double.IsNaN(easing.Evaluate(0.5d)));
            Assert.IsFalse(easing.TryToAnimationCurve(2, out AnimationCurve curve));
            Assert.IsTrue(curve == null);
            Assert.IsTrue(easing.ToAnimationCurve() == null);
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void NonfiniteInputsReturnNaN(double x)
        {
            Assert.IsTrue(double.IsNaN(default(CubicBezier).Evaluate(x)));
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(9)]
        [TestCase(64)]
        public void WeightedCurveMatchesEvaluation(int keyCount)
        {
            CubicBezier easing = new(0.25f, -0.4f, 0.7f, 1.3f);
            Assert.IsTrue(easing.TryToAnimationCurve(keyCount, out AnimationCurve curve));
            Assert.IsTrue(curve != null);
            Assert.AreEqual(keyCount, curve.length);
            for (int sample = 0; sample <= 100; ++sample)
            {
                float x = sample / 100f;
                Assert.AreEqual(
                    easing.Evaluate(x),
                    curve.Evaluate(x),
                    2e-4,
                    $"x={x}, keys={keyCount}"
                );
            }
            Assert.AreEqual(0f, curve.Evaluate(-1f));
            Assert.AreEqual(1f, curve.Evaluate(2f));
        }

        [TestCase(0f, 1f, 0.5f, 1f, 2)]
        [TestCase(0.5f, 0f, 1f, 0f, 2)]
        [TestCase(1f, 0f, 0f, 1f, 3)]
        [TestCase(float.Epsilon, 1f, 0.5f, 1f, 2)]
        [TestCase(1f, 0f, 1f, 1f, CubicBezier.MaximumKeyCount)]
        public void UnrepresentableHandlesOrTimesFail(
            float firstX,
            float firstY,
            float secondX,
            float secondY,
            int keyCount
        )
        {
            CubicBezier easing = new(firstX, firstY, secondX, secondY);
            Assert.IsTrue(easing.IsValid);
            Assert.IsFalse(easing.TryToAnimationCurve(keyCount, out AnimationCurve curve));
            Assert.IsTrue(curve == null);
        }

        [TestCase(int.MinValue)]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(CubicBezier.MaximumKeyCount + 1)]
        [TestCase(int.MaxValue)]
        public void InvalidKeyCountsFailBeforeAllocation(int keyCount)
        {
            Assert.IsTrue(default(CubicBezier).ToAnimationCurve(keyCount) == null);
        }

        [Test]
        public void DefaultCurveHasValidCollapsedHandles()
        {
            CubicBezier easing = default;
            Assert.IsTrue(easing.IsValid);
            AnimationCurve curve = easing.ToAnimationCurve();
            Assert.IsTrue(curve != null);
            Assert.AreEqual(0.25f, curve.Evaluate(0.25f), 1e-5f);
        }

        [Test]
        public void EqualityUsesAllControls()
        {
            CubicBezier first = new(0.2f, 0.3f, 0.4f, 0.5f);
            CubicBezier equal = new(0.2f, 0.3f, 0.4f, 0.5f);
            Assert.IsTrue(first.Equals(equal));
            Assert.IsTrue(first.Equals((object)equal));
            Assert.AreEqual(first.GetHashCode(), equal.GetHashCode());
            Assert.IsFalse(first.Equals(null));
            Assert.IsFalse(first.Equals(new CubicBezier(0.1f, 0.3f, 0.4f, 0.5f)));
            Assert.IsFalse(first.Equals(new CubicBezier(0.2f, 0.1f, 0.4f, 0.5f)));
            Assert.IsFalse(first.Equals(new CubicBezier(0.2f, 0.3f, 0.1f, 0.5f)));
            Assert.IsFalse(first.Equals(new CubicBezier(0.2f, 0.3f, 0.4f, 0.1f)));
        }
    }
}
