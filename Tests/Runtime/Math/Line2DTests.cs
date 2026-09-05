// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Math
{
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Math;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class Line2DTests
    {
        private const float Epsilon = 0.0001f;

        [Test]
        public void ConstructorCreatesLineWithCorrectEndpoints()
        {
            Vector2 from = new(0f, 0f);
            Vector2 to = new(10f, 10f);
            Line2D line = new(from, to);

            Assert.AreEqual(from, line.from);
            Assert.AreEqual(to, line.to);
        }

        [Test]
        public void LengthReturnsCorrectValue()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(3f, 4f));
            Assert.AreEqual(5f, line.Length, Epsilon);
        }

        [Test]
        public void LengthSquaredReturnsCorrectValue()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(3f, 4f));
            Assert.AreEqual(25f, line.LengthSquared, Epsilon);
        }

        [Test]
        public void LengthSquaredMatchesLengthSquared()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(100f, 100f));
            float lengthSq = line.LengthSquared;
            float length = line.Length;
            Assert.AreEqual(length * length, lengthSq, Epsilon);
        }

        [Test]
        public void DirectionReturnsCorrectVector()
        {
            Line2D line = new(new Vector2(1f, 2f), new Vector2(4f, 6f));
            Vector2 expected = new(3f, 4f);
            Assert.AreEqual(expected, line.Direction);
        }

        [Test]
        public void NormalizedDirectionReturnsUnitVector()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(3f, 4f));
            Vector2 normalized = line.NormalizedDirection;
            Assert.AreEqual(1f, normalized.magnitude, Epsilon);
            Assert.AreEqual(0.6f, normalized.x, Epsilon);
            Assert.AreEqual(0.8f, normalized.y, Epsilon);
        }

        [Test]
        public void IntersectsReturnsTrueForCrossingLines()
        {
            Line2D line1 = new(new Vector2(0f, 0f), new Vector2(10f, 10f));
            Line2D line2 = new(new Vector2(0f, 10f), new Vector2(10f, 0f));
            Assert.IsTrue(line1.Intersects(line2));
        }

        [Test]
        public void IntersectsReturnsFalseForParallelLines()
        {
            Line2D line1 = new(new Vector2(0f, 0f), new Vector2(10f, 0f));
            Line2D line2 = new(new Vector2(0f, 1f), new Vector2(10f, 1f));
            Assert.IsFalse(line1.Intersects(line2));
        }

        [Test]
        public void IntersectsReturnsFalseForNonIntersectingLines()
        {
            Line2D line1 = new(new Vector2(0f, 0f), new Vector2(1f, 1f));
            Line2D line2 = new(new Vector2(5f, 5f), new Vector2(6f, 6f));
            Assert.IsFalse(line1.Intersects(line2));
        }

        [Test]
        public void TryGetIntersectionPointReturnsTrueForCrossingLines()
        {
            Line2D line1 = new(new Vector2(0f, 0f), new Vector2(10f, 10f));
            Line2D line2 = new(new Vector2(0f, 10f), new Vector2(10f, 0f));

            bool result = line1.TryGetIntersectionPoint(line2, out Vector2 intersection);

            Assert.IsTrue(result);
            Assert.AreEqual(5f, intersection.x, Epsilon);
            Assert.AreEqual(5f, intersection.y, Epsilon);
        }

        [Test]
        public void TryGetIntersectionPointReturnsFalseForParallelLines()
        {
            Line2D line1 = new(new Vector2(0f, 0f), new Vector2(10f, 0f));
            Line2D line2 = new(new Vector2(0f, 1f), new Vector2(10f, 1f));

            bool result = line1.TryGetIntersectionPoint(line2, out Vector2 intersection);

            Assert.IsFalse(result);
            Assert.AreEqual(Vector2.zero, intersection);
        }

        [Test]
        public void TryGetIntersectionPointReturnsFalseForNonIntersectingLines()
        {
            Line2D line1 = new(new Vector2(0f, 0f), new Vector2(1f, 1f));
            Line2D line2 = new(new Vector2(5f, 5f), new Vector2(6f, 6f));

            bool result = line1.TryGetIntersectionPoint(line2, out Vector2 intersection);

            Assert.IsFalse(result);
            Assert.AreEqual(Vector2.zero, intersection);
        }

        [Test]
        public void TryGetIntersectionPointHandlesVerticalAndHorizontalLines()
        {
            Line2D vertical = new(new Vector2(5f, 0f), new Vector2(5f, 10f));
            Line2D horizontal = new(new Vector2(0f, 5f), new Vector2(10f, 5f));

            bool result = vertical.TryGetIntersectionPoint(horizontal, out Vector2 intersection);

            Assert.IsTrue(result);
            Assert.AreEqual(5f, intersection.x, Epsilon);
            Assert.AreEqual(5f, intersection.y, Epsilon);
        }

        [Test]
        public void TryGetIntersectionPointHandlesCollinearOverlappingLines()
        {
            Line2D line1 = new(new Vector2(0f, 0f), new Vector2(10f, 0f));
            Line2D line2 = new(new Vector2(5f, 0f), new Vector2(15f, 0f));

            bool result = line1.TryGetIntersectionPoint(line2, out _);

            Assert.IsFalse(result);
        }

        [Test]
        public void TryGetIntersectionPointHandlesTShapeIntersection()
        {
            Line2D line1 = new(new Vector2(0f, 5f), new Vector2(10f, 5f));
            Line2D line2 = new(new Vector2(5f, 0f), new Vector2(5f, 5f));

            bool result = line1.TryGetIntersectionPoint(line2, out Vector2 intersection);

            Assert.IsTrue(result);
            Assert.AreEqual(5f, intersection.x, Epsilon);
            Assert.AreEqual(5f, intersection.y, Epsilon);
        }

        [Test]
        public void TryGetIntersectionPointHandlesEndpointTouching()
        {
            Line2D line1 = new(new Vector2(0f, 0f), new Vector2(5f, 5f));
            Line2D line2 = new(new Vector2(5f, 5f), new Vector2(10f, 0f));

            bool result = line1.TryGetIntersectionPoint(line2, out Vector2 intersection);

            Assert.IsTrue(result);
            Assert.AreEqual(5f, intersection.x, Epsilon);
            Assert.AreEqual(5f, intersection.y, Epsilon);
        }

        [Test]
        public void TryGetIntersectionPointPrecisionTest()
        {
            Line2D line1 = new(new Vector2(0f, 0f), new Vector2(100f, 100f));
            Line2D line2 = new(new Vector2(0f, 100f), new Vector2(100f, 0f));

            bool result = line1.TryGetIntersectionPoint(line2, out Vector2 intersection);

            Assert.IsTrue(result);
            Assert.AreEqual(50f, intersection.x, Epsilon);
            Assert.AreEqual(50f, intersection.y, Epsilon);
        }

        [Test]
        public void DistanceToPointReturnsZeroForPointOnLine()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(10f, 0f));
            Vector2 point = new(5f, 0f);
            Assert.AreEqual(0f, line.DistanceToPoint(point), Epsilon);
        }

        [Test]
        public void DistanceToPointReturnsPerpendicularDistance()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(10f, 0f));
            Vector2 point = new(5f, 3f);
            Assert.AreEqual(3f, line.DistanceToPoint(point), Epsilon);
        }

        [Test]
        public void DistanceToPointHandlesEndpointProjection()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(10f, 0f));
            Vector2 point = new(15f, 5f);
            float distance = line.DistanceToPoint(point);
            float expected = Mathf.Sqrt(25f + 25f);
            Assert.AreEqual(expected, distance, Epsilon);
        }

        [Test]
        public void ClosestPointOnLineReturnsPointOnSegment()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(10f, 0f));
            Vector2 point = new(5f, 3f);
            Vector2 closest = line.ClosestPointOnLine(point);

            Assert.AreEqual(5f, closest.x, Epsilon);
            Assert.AreEqual(0f, closest.y, Epsilon);
        }

        [Test]
        public void ClosestPointOnLineReturnsSamePointWhenOnLine()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(10f, 10f));
            Vector2 point = new(5f, 5f);
            Vector2 closest = line.ClosestPointOnLine(point);

            Assert.AreEqual(point, closest);
        }

        [Test]
        public void ClosestPointOnLineClampsToEndpoints()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(10f, 0f));
            Vector2 point = new(15f, 5f);
            Vector2 closest = line.ClosestPointOnLine(point);

            Assert.AreEqual(10f, closest.x, Epsilon);
            Assert.AreEqual(0f, closest.y, Epsilon);
        }

        [Test]
        public void ClosestPointOnLineHandlesZeroLengthLine()
        {
            Line2D line = new(new Vector2(5f, 5f), new Vector2(5f, 5f));
            Vector2 point = new(10f, 10f);
            Vector2 closest = line.ClosestPointOnLine(point);

            Assert.AreEqual(5f, closest.x, Epsilon);
            Assert.AreEqual(5f, closest.y, Epsilon);
        }

        [Test]
        public void ClosestPointOnLineDiagonalTest()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(10f, 10f));
            Vector2 point = new(10f, 0f);
            Vector2 closest = line.ClosestPointOnLine(point);

            Assert.AreEqual(5f, closest.x, Epsilon);
            Assert.AreEqual(5f, closest.y, Epsilon);
        }

        [Test]
        public void ContainsReturnsTrueForPointOnLine()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(10f, 10f));
            Vector2 point = new(5f, 5f);
            Assert.IsTrue(line.Contains(point));
        }

        [Test]
        public void ContainsReturnsTrueForEndpoints()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(10f, 10f));
            Assert.IsTrue(line.Contains(line.from));
            Assert.IsTrue(line.Contains(line.to));
        }

        [Test]
        public void ContainsReturnsFalseForPointNotOnLine()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(10f, 10f));
            Vector2 point = new(5f, 6f);
            Assert.IsFalse(line.Contains(point));
        }

        [Test]
        public void ContainsReturnsFalseForCollinearPointOutsideSegment()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(10f, 10f));
            Vector2 point = new(15f, 15f);
            Assert.IsFalse(line.Contains(point));
        }

        [Test]
        public void ContainsHandlesHorizontalLine()
        {
            Line2D line = new(new Vector2(0f, 5f), new Vector2(10f, 5f));
            Assert.IsTrue(line.Contains(new Vector2(5f, 5f)));
            Assert.IsFalse(line.Contains(new Vector2(5f, 6f)));
            Assert.IsFalse(line.Contains(new Vector2(15f, 5f)));
        }

        [Test]
        public void ContainsHandlesVerticalLine()
        {
            Line2D line = new(new Vector2(5f, 0f), new Vector2(5f, 10f));
            Assert.IsTrue(line.Contains(new Vector2(5f, 5f)));
            Assert.IsFalse(line.Contains(new Vector2(6f, 5f)));
            Assert.IsFalse(line.Contains(new Vector2(5f, 15f)));
        }

        [Test]
        public void EqualsReturnsTrueForSameLine()
        {
            Line2D line1 = new(new Vector2(0f, 0f), new Vector2(10f, 10f));
            Line2D line2 = new(new Vector2(0f, 0f), new Vector2(10f, 10f));
            Assert.IsTrue(line1.Equals(line2));
        }

        [Test]
        public void EqualsReturnsFalseForDifferentLines()
        {
            Line2D line1 = new(new Vector2(0f, 0f), new Vector2(10f, 10f));
            Line2D line2 = new(new Vector2(0f, 0f), new Vector2(5f, 5f));
            Assert.IsFalse(line1.Equals(line2));
        }

        [Test]
        public void EqualsReturnsFalseForReversedLine()
        {
            Line2D line1 = new(new Vector2(0f, 0f), new Vector2(10f, 10f));
            Line2D line2 = new(new Vector2(10f, 10f), new Vector2(0f, 0f));
            Assert.IsFalse(line1.Equals(line2));
        }

        [Test]
        public void EqualsIsExactSoNearlyIdenticalEndpointsDiffer()
        {
            Line2D line1 = new(new Vector2(0f, 0f), new Vector2(10f, 10f));
            Line2D line2 = new(new Vector2(1e-6f, 0f), new Vector2(10f, 10f));

            // Approximate vector equality previously placed equal lines in different hash buckets.
            Assert.IsFalse(line1.Equals(line2));
            Assert.IsTrue(line1 != line2);
            Assert.IsTrue(line1.ApproximatelyEquals(line2, 1e-4f));
        }

        [Test]
        public void ApproximatelyEqualsComparesBothEndpointsInOrder()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(10f, 10f));

            Assert.IsTrue(
                line.ApproximatelyEquals(
                    new Line2D(new Vector2(0.001f, 0f), new Vector2(10f, 10f)),
                    0.01f
                )
            );
            Assert.IsFalse(
                line.ApproximatelyEquals(
                    new Line2D(new Vector2(0f, 0f), new Vector2(10.1f, 10f)),
                    0.01f
                )
            );
            Assert.IsFalse(
                line.ApproximatelyEquals(
                    new Line2D(new Vector2(10f, 10f), new Vector2(0f, 0f)),
                    0.01f
                )
            );
        }

        [Test]
        [TestCase(-1f, TestName = "Tolerance.Negative.ReturnsFalse")]
        [TestCase(float.NaN, TestName = "Tolerance.NotANumber.ReturnsFalse")]
        [TestCase(float.PositiveInfinity, TestName = "Tolerance.Infinite.ReturnsFalse")]
        public void ApproximatelyEqualsRefusesAnInvalidTolerance(float tolerance)
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(10f, 10f));

            Assert.IsFalse(line.ApproximatelyEquals(line, tolerance));
        }

        [Test]
        public void ApproximatelyEqualsAdmitsNothingBeyondTheToleranceAtALargeMagnitude()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(1_000_000f, 10f));
            Line2D nudged = new(new Vector2(0f, 0f), new Vector2(1_000_000.5f, 10f));

            Assert.IsFalse(line.ApproximatelyEquals(nudged, 0f));
            Assert.IsFalse(line.ApproximatelyEquals(nudged, 0.25f));
            Assert.IsTrue(line.ApproximatelyEquals(nudged, 0.5f));
        }

        [Test]
        public void ApproximatelyEqualsComparesANonFiniteCoordinateExactly()
        {
            Line2D infinite = new(Vector2.zero, new Vector2(float.PositiveInfinity, 10f));
            Line2D negativelyInfinite = new(Vector2.zero, new Vector2(float.NegativeInfinity, 10f));

            Assert.IsTrue(
                infinite.ApproximatelyEquals(infinite, 1f),
                "A line must be approximately equal to itself whatever it holds"
            );
            Assert.IsFalse(infinite.ApproximatelyEquals(negativelyInfinite, 1f));
        }

        [Test]
        public void OperatorEqualsWorks()
        {
            Line2D line1 = new(new Vector2(0f, 0f), new Vector2(10f, 10f));
            Line2D line2 = new(new Vector2(0f, 0f), new Vector2(10f, 10f));
            Line2D line3 = new(new Vector2(1f, 1f), new Vector2(10f, 10f));

            Assert.IsTrue(line1 == line2);
            Assert.IsFalse(line1 == line3);
        }

        [Test]
        public void OperatorNotEqualsWorks()
        {
            Line2D line1 = new(new Vector2(0f, 0f), new Vector2(10f, 10f));
            Line2D line2 = new(new Vector2(0f, 0f), new Vector2(10f, 10f));
            Line2D line3 = new(new Vector2(1f, 1f), new Vector2(10f, 10f));

            Assert.IsFalse(line1 != line2);
            Assert.IsTrue(line1 != line3);
        }

        [Test]
        public void GetHashCodeReturnsSameValueForEqualLines()
        {
            Line2D line1 = new(new Vector2(0f, 0f), new Vector2(10f, 10f));
            Line2D line2 = new(new Vector2(0f, 0f), new Vector2(10f, 10f));
            Assert.AreEqual(line1.GetHashCode(), line2.GetHashCode());
        }

        [Test]
        public void GetHashCodeReturnsDifferentValuesForDifferentLines()
        {
            Line2D line1 = new(new Vector2(0f, 0f), new Vector2(10f, 10f));
            Line2D line2 = new(new Vector2(0f, 0f), new Vector2(5f, 5f));
            Assert.AreNotEqual(line1.GetHashCode(), line2.GetHashCode());
        }

        [Test]
        public void ToStringReturnsFormattedString()
        {
            Line2D line = new(new Vector2(1f, 2f), new Vector2(3f, 4f));
            string result = line.ToString();
            Assert.IsTrue(result.Contains("Line2D"));
            Assert.IsTrue(result.Contains("from"));
            Assert.IsTrue(result.Contains("to"));
        }

        [Test]
        public void ZeroLengthLineHasZeroLength()
        {
            Line2D line = new(new Vector2(5f, 5f), new Vector2(5f, 5f));
            Assert.AreEqual(0f, line.Length, Epsilon);
            Assert.AreEqual(0f, line.LengthSquared, Epsilon);
        }

        [Test]
        public void ZeroLengthLineDirectionIsZero()
        {
            Line2D line = new(new Vector2(5f, 5f), new Vector2(5f, 5f));
            Assert.AreEqual(Vector2.zero, line.Direction);
        }

        [Test]
        public void VerySmallLineSegmentWorks()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(0.0001f, 0.0001f));
            Assert.Greater(line.Length, 0f);
            Assert.IsTrue(line.Contains(new Vector2(0.00005f, 0.00005f)));
        }

        [Test]
        public void VeryLargeLineSegmentWorks()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(1000000f, 1000000f));
            Assert.AreEqual(Mathf.Sqrt(2f * 1000000f * 1000000f), line.Length, 1f);
        }

        [Test]
        public void NegativeCoordinatesWork()
        {
            Line2D line = new(new Vector2(-10f, -10f), new Vector2(10f, 10f));
            Assert.AreEqual(new Vector2(0f, 0f), line.ClosestPointOnLine(new Vector2(0f, 0f)));
        }

        [Test]
        public void IntersectionOfCrossPatternWorks()
        {
            Line2D vertical = new(new Vector2(5f, 0f), new Vector2(5f, 10f));
            Line2D horizontal = new(new Vector2(0f, 5f), new Vector2(10f, 5f));
            Line2D diagonal1 = new(new Vector2(0f, 0f), new Vector2(10f, 10f));
            Line2D diagonal2 = new(new Vector2(0f, 10f), new Vector2(10f, 0f));

            Assert.IsTrue(vertical.TryGetIntersectionPoint(horizontal, out Vector2 p1));
            Assert.IsTrue(diagonal1.TryGetIntersectionPoint(diagonal2, out Vector2 p2));

            Assert.AreEqual(5f, p1.x, Epsilon);
            Assert.AreEqual(5f, p1.y, Epsilon);
            Assert.AreEqual(5f, p2.x, Epsilon);
            Assert.AreEqual(5f, p2.y, Epsilon);
        }

        [Test]
        public void TriangleEdgesIntersectionTest()
        {
            Line2D edge1 = new(new Vector2(0f, 0f), new Vector2(10f, 0f));
            Line2D edge2 = new(new Vector2(10f, 0f), new Vector2(5f, 8.66f));
            Line2D edge3 = new(new Vector2(5f, 8.66f), new Vector2(0f, 0f));

            Assert.IsFalse(
                edge1.TryGetIntersectionPoint(edge2, out _) && !edge1.to.Equals(edge2.from)
            );

            Line2D centerLine = new(new Vector2(5f, 0f), new Vector2(5f, 10f));
            Assert.IsTrue(centerLine.TryGetIntersectionPoint(edge1, out _));
            Assert.IsTrue(centerLine.TryGetIntersectionPoint(edge3, out _));
        }

        [Test]
        public void IntersectsCircleReturnsTrueForLineThroughCircle()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(10f, 0f));
            Circle circle = new(new Vector2(5f, 0f), 2f);
            Assert.IsTrue(line.Intersects(circle));
        }

        [Test]
        public void IntersectsCircleReturnsTrueForLineTouchingCircle()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(10f, 0f));
            Circle circle = new(new Vector2(5f, 3f), 3f);
            Assert.IsTrue(line.Intersects(circle));
        }

        [Test]
        public void IntersectsCircleReturnsFalseForNonIntersectingCircle()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(10f, 0f));
            Circle circle = new(new Vector2(5f, 5f), 2f);
            Assert.IsFalse(line.Intersects(circle));
        }

        [Test]
        public void IntersectsCircleReturnsTrueForCircleContainingEndpoint()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(10f, 0f));
            Circle circle = new(new Vector2(0f, 0f), 1f);
            Assert.IsTrue(line.Intersects(circle));
        }

        [Test]
        public void IntersectsCircleReturnsFalseForCircleNearLineSegmentButNotTouching()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(5f, 0f));
            Circle circle = new(new Vector2(10f, 0f), 2f);
            Assert.IsFalse(line.Intersects(circle));
        }

        [Test]
        public void DistanceToCircleReturnsZeroForIntersectingCircle()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(10f, 0f));
            Circle circle = new(new Vector2(5f, 0f), 2f);
            Assert.AreEqual(0f, line.DistanceToCircle(circle), Epsilon);
        }

        [Test]
        public void DistanceToCircleReturnsCorrectDistanceForNonIntersectingCircle()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(10f, 0f));
            Circle circle = new(new Vector2(5f, 5f), 2f);
            Assert.AreEqual(3f, line.DistanceToCircle(circle), Epsilon);
        }

        [Test]
        public void DistanceToCircleReturnsZeroForCircleTouchingLine()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(10f, 0f));
            Circle circle = new(new Vector2(5f, 3f), 3f);
            Assert.AreEqual(0f, line.DistanceToCircle(circle), Epsilon);
        }

        [Test]
        public void DistanceSquaredToPointReturnsZeroForPointOnLine()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(10f, 0f));
            Vector2 point = new(5f, 0f);
            Assert.AreEqual(0f, line.DistanceSquaredToPoint(point), Epsilon);
        }

        [Test]
        public void DistanceSquaredToPointReturnsSquaredDistance()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(10f, 0f));
            Vector2 point = new(5f, 3f);
            Assert.AreEqual(9f, line.DistanceSquaredToPoint(point), Epsilon);
        }

        [Test]
        public void DistanceSquaredToPointMatchesDistanceSquared()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(10f, 0f));
            Vector2 point = new(5f, 4f);
            float distance = line.DistanceToPoint(point);
            float distanceSquared = line.DistanceSquaredToPoint(point);
            Assert.AreEqual(distance * distance, distanceSquared, Epsilon);
        }

        [Test]
        public void CircleIntersectionHandlesVerticalLine()
        {
            Line2D line = new(new Vector2(5f, 0f), new Vector2(5f, 10f));
            Circle circle = new(new Vector2(5f, 5f), 2f);
            Assert.IsTrue(line.Intersects(circle));
        }

        [Test]
        public void CircleIntersectionHandlesDiagonalLine()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(10f, 10f));
            Circle circle = new(new Vector2(5f, 5f), 1f);
            Assert.IsTrue(line.Intersects(circle));
        }

        [Test]
        public void CircleIntersectionHandlesSmallCircle()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(10f, 0f));
            Circle circle = new(new Vector2(5f, 0.001f), 0.01f);
            Assert.IsTrue(line.Intersects(circle));
        }

        [Test]
        public void CircleIntersectionHandlesLargeCircle()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(1f, 0f));
            Circle circle = new(new Vector2(0f, 0f), 1000f);
            Assert.IsTrue(line.Intersects(circle));
        }

        [Test]
        public void DistanceToCircleHandlesCircleFarFromLineSegment()
        {
            Line2D line = new(new Vector2(0f, 0f), new Vector2(1f, 0f));
            Circle circle = new(new Vector2(10f, 10f), 2f);
            Vector2 closestPointOnLine = new(1f, 0f);
            float distanceToCenter = Vector2.Distance(closestPointOnLine, circle.center);
            float expected = distanceToCenter - circle.radius;
            Assert.AreEqual(expected, line.DistanceToCircle(circle), 0.01f);
        }
    }
}
