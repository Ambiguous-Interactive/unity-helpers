// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;

    [TestFixture]
    [Category("Fast")]
    public sealed class ShapeHelperTests
    {
        private static void AssertPointEquals(Vector2 expected, Vector2 actual)
        {
            Assert.AreEqual(expected.x, actual.x, 0.00001f);
            Assert.AreEqual(expected.y, actual.y, 0.00001f);
        }

        [Test]
        public void CircularCurveIncludesBothEndpoints()
        {
            Vector2 center = new(2f, -3f);

            List<Vector2> points = ShapeHelper.GenerateCircularCurvePoints(center, 2f, 0f, 180f, 3);

            AssertPointEquals(new Vector2(4f, -3f), points[0]);
            AssertPointEquals(new Vector2(2f, -1f), points[1]);
            AssertPointEquals(new Vector2(0f, -3f), points[2]);
        }

        [Test]
        public void SinglePointUsesArcMidpoint()
        {
            List<Vector2> points = ShapeHelper.GenerateCircularCurvePoints(
                Vector2.zero,
                3f,
                10f,
                170f,
                1
            );

            AssertPointEquals(new Vector2(0f, 3f), points[0]);
        }

        [Test]
        public void DescendingCurvePreservesDirection()
        {
            List<Vector2> points = ShapeHelper.GenerateCircularCurvePoints(
                Vector2.zero,
                1f,
                180f,
                0f,
                3
            );

            AssertPointEquals(Vector2.left, points[0]);
            AssertPointEquals(Vector2.up, points[1]);
            AssertPointEquals(Vector2.right, points[2]);
        }

        [Test]
        public void CircularCurveClearsAndReturnsCallerBuffer()
        {
            List<Vector2> buffer = new() { new Vector2(100f, 100f) };

            List<Vector2> result = ShapeHelper.GenerateCircularCurvePoints(
                Vector2.zero,
                1f,
                0f,
                90f,
                2,
                buffer
            );

            Assert.AreSame(buffer, result);
            Assert.AreEqual(2, result.Count);
            AssertPointEquals(Vector2.right, result[0]);
            AssertPointEquals(Vector2.up, result[1]);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void InvalidPointCountReturnsEmptyCallerBuffer(int pointCount)
        {
            List<Vector2> buffer = new() { Vector2.one };

            List<Vector2> result = ShapeHelper.GenerateCircularCurvePoints(
                Vector2.zero,
                1f,
                0f,
                360f,
                pointCount,
                buffer
            );

            Assert.AreSame(buffer, result);
            Assert.IsEmpty(result);
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void InvalidRadiusReturnsEmptyCallerBuffer(float radius)
        {
            List<Vector2> buffer = new() { Vector2.one };

            List<Vector2> result = ShapeHelper.GenerateCircularCurvePoints(
                Vector2.zero,
                radius,
                0f,
                360f,
                4,
                buffer
            );

            Assert.AreSame(buffer, result);
            Assert.IsEmpty(result);
        }

        [Test]
        public void TopCircularFractionCentersArcOnUp()
        {
            List<Vector2> points = ShapeHelper.GenerateTopCircularFraction(
                Vector2.zero,
                2f,
                0.5f,
                3
            );

            float diagonal = Mathf.Sqrt(2f);
            AssertPointEquals(new Vector2(diagonal, diagonal), points[0]);
            AssertPointEquals(new Vector2(0f, 2f), points[1]);
            AssertPointEquals(new Vector2(-diagonal, diagonal), points[2]);
        }

        [Test]
        public void CircularFractionCentersArcOnRequestedAngle()
        {
            List<Vector2> points = ShapeHelper.GenerateCircularFraction(
                Vector2.zero,
                1f,
                0.25f,
                180f,
                3
            );

            float diagonal = Mathf.Sqrt(0.5f);
            AssertPointEquals(new Vector2(-diagonal, diagonal), points[0]);
            AssertPointEquals(Vector2.left, points[1]);
            AssertPointEquals(new Vector2(-diagonal, -diagonal), points[2]);
        }

        [Test]
        public void FullCircularFractionDuplicatesGeometricEndpoint()
        {
            List<Vector2> points = ShapeHelper.GenerateCircularFraction(
                Vector2.zero,
                1f,
                1f,
                90f,
                5
            );

            AssertPointEquals(points[0], points[points.Count - 1]);
        }

        [Test]
        public void NonFiniteGeometryReturnsEmptyResult()
        {
            float[] invalidValues = { float.NaN, float.PositiveInfinity, float.NegativeInfinity };

            foreach (float invalidValue in invalidValues)
            {
                Assert.IsEmpty(
                    ShapeHelper.GenerateCircularCurvePoints(
                        new Vector2(invalidValue, 0f),
                        1f,
                        0f,
                        90f,
                        3
                    )
                );
                Assert.IsEmpty(
                    ShapeHelper.GenerateCircularCurvePoints(Vector2.zero, 1f, invalidValue, 90f, 3)
                );
                Assert.IsEmpty(
                    ShapeHelper.GenerateCircularFraction(Vector2.zero, 1f, 0.5f, invalidValue, 3)
                );
            }
        }

        [Test]
        public void ExtremeFiniteAnglesProduceFinitePoints()
        {
            List<Vector2> points = ShapeHelper.GenerateCircularCurvePoints(
                Vector2.zero,
                1f,
                -float.MaxValue,
                float.MaxValue,
                3
            );

            Assert.AreEqual(3, points.Count);
            foreach (Vector2 point in points)
            {
                Assert.IsFalse(float.IsNaN(point.x));
                Assert.IsFalse(float.IsInfinity(point.x));
                Assert.IsFalse(float.IsNaN(point.y));
                Assert.IsFalse(float.IsInfinity(point.y));
            }
        }

        [TestCase(0f)]
        [TestCase(-0.1f)]
        [TestCase(1.1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void InvalidCircularFractionsReturnEmptyResults(float fraction)
        {
            List<Vector2> topBuffer = new() { Vector2.one };
            List<Vector2> circularBuffer = new() { Vector2.one };

            List<Vector2> topResult = ShapeHelper.GenerateTopCircularFraction(
                Vector2.zero,
                1f,
                fraction,
                3,
                topBuffer
            );
            List<Vector2> circularResult = ShapeHelper.GenerateCircularFraction(
                Vector2.zero,
                1f,
                fraction,
                90f,
                3,
                circularBuffer
            );

            Assert.AreSame(topBuffer, topResult);
            Assert.AreSame(circularBuffer, circularResult);
            Assert.IsEmpty(topResult);
            Assert.IsEmpty(circularResult);
        }
    }
}
