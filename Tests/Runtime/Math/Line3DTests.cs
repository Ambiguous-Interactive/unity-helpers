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
    public sealed class Line3DTests
    {
        private const float Epsilon = 0.0001f;

        [Test]
        public void ConstructorCreatesLineWithCorrectEndpoints()
        {
            Vector3 from = new(0f, 0f, 0f);
            Vector3 to = new(10f, 10f, 10f);
            Line3D line = new(from, to);

            Assert.AreEqual(from, line.from);
            Assert.AreEqual(to, line.to);
        }

        [Test]
        public void LengthReturnsCorrectValue()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(2f, 3f, 6f));
            Assert.AreEqual(7f, line.Length, Epsilon);
        }

        [Test]
        public void LengthSquaredReturnsCorrectValue()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(2f, 3f, 6f));
            Assert.AreEqual(49f, line.LengthSquared, Epsilon);
        }

        [Test]
        public void LengthSquaredMatchesLengthSquared()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(100f, 100f, 100f));
            float lengthSq = line.LengthSquared;
            float length = line.Length;
            Assert.AreEqual(length * length, lengthSq, 0.01f);
        }

        [Test]
        public void DirectionReturnsCorrectVector()
        {
            Line3D line = new(new Vector3(1f, 2f, 3f), new Vector3(4f, 6f, 9f));
            Vector3 expected = new(3f, 4f, 6f);
            Assert.AreEqual(expected, line.Direction);
        }

        [Test]
        public void NormalizedDirectionReturnsUnitVector()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(2f, 3f, 6f));
            Vector3 normalized = line.NormalizedDirection;
            Assert.AreEqual(1f, normalized.magnitude, Epsilon);
        }

        [Test]
        public void DistanceToPointReturnsZeroForPointOnLine()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Vector3 point = new(5f, 0f, 0f);
            Assert.AreEqual(0f, line.DistanceToPoint(point), Epsilon);
        }

        [Test]
        public void DistanceToPointReturnsPerpendicularDistance()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Vector3 point = new(5f, 3f, 4f);
            Assert.AreEqual(5f, line.DistanceToPoint(point), Epsilon);
        }

        [Test]
        public void DistanceToPointHandlesEndpointProjection()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Vector3 point = new(15f, 3f, 4f);
            float distance = line.DistanceToPoint(point);
            float expected = Mathf.Sqrt(25f + 9f + 16f);
            Assert.AreEqual(expected, distance, Epsilon);
        }

        [Test]
        public void ClosestPointOnLineReturnsPointOnSegment()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Vector3 point = new(5f, 3f, 4f);
            Vector3 closest = line.ClosestPointOnLine(point);

            Assert.AreEqual(5f, closest.x, Epsilon);
            Assert.AreEqual(0f, closest.y, Epsilon);
            Assert.AreEqual(0f, closest.z, Epsilon);
        }

        [Test]
        public void ClosestPointOnLineReturnsSamePointWhenOnLine()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f));
            Vector3 point = new(5f, 5f, 5f);
            Vector3 closest = line.ClosestPointOnLine(point);

            Assert.AreEqual(point, closest);
        }

        [Test]
        public void ClosestPointOnLineClampsToEndpoints()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Vector3 point = new(15f, 5f, 5f);
            Vector3 closest = line.ClosestPointOnLine(point);

            Assert.AreEqual(10f, closest.x, Epsilon);
            Assert.AreEqual(0f, closest.y, Epsilon);
            Assert.AreEqual(0f, closest.z, Epsilon);
        }

        [Test]
        public void ClosestPointOnLineHandlesZeroLengthLine()
        {
            Line3D line = new(new Vector3(5f, 5f, 5f), new Vector3(5f, 5f, 5f));
            Vector3 point = new(10f, 10f, 10f);
            Vector3 closest = line.ClosestPointOnLine(point);

            Assert.AreEqual(5f, closest.x, Epsilon);
            Assert.AreEqual(5f, closest.y, Epsilon);
            Assert.AreEqual(5f, closest.z, Epsilon);
        }

        [Test]
        public void ClosestPointOnLineDiagonalTest()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f));
            Vector3 point = new(10f, 0f, 0f);
            Vector3 closest = line.ClosestPointOnLine(point);

            Assert.AreEqual(3.333f, closest.x, 0.01f);
            Assert.AreEqual(3.333f, closest.y, 0.01f);
            Assert.AreEqual(3.333f, closest.z, 0.01f);
        }

        [Test]
        public void ContainsReturnsTrueForPointOnLine()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f));
            Vector3 point = new(5f, 5f, 5f);
            Assert.IsTrue(line.Contains(point));
        }

        [Test]
        public void ContainsReturnsTrueForEndpoints()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f));
            Assert.IsTrue(line.Contains(line.from));
            Assert.IsTrue(line.Contains(line.to));
        }

        [Test]
        public void ContainsReturnsFalseForPointNotOnLine()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f));
            Vector3 point = new(5f, 6f, 7f);
            Assert.IsFalse(line.Contains(point));
        }

        [Test]
        public void ContainsReturnsFalseForCollinearPointOutsideSegment()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f));
            Vector3 point = new(15f, 15f, 15f);
            Assert.IsFalse(line.Contains(point));
        }

        [Test]
        public void ContainsHandlesCustomTolerance()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Vector3 point = new(5f, 0.01f, 0f);
            Assert.IsTrue(line.Contains(point, 0.1f));
            Assert.IsFalse(line.Contains(point, 0.001f));
        }

        [Test]
        public void TryGetClosestPointsReturnsCorrectPointsForSkewLines()
        {
            Line3D line1 = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Line3D line2 = new(new Vector3(5f, 5f, 5f), new Vector3(5f, -5f, 5f));

            bool result = line1.TryGetClosestPoints(
                line2,
                out Vector3 closest1,
                out Vector3 closest2
            );

            Assert.IsTrue(result);
            Assert.AreEqual(5f, closest1.x, Epsilon);
            Assert.AreEqual(0f, closest1.y, Epsilon);
            Assert.AreEqual(0f, closest1.z, Epsilon);
            Assert.AreEqual(5f, closest2.x, Epsilon);
            Assert.AreEqual(0f, closest2.y, Epsilon);
            Assert.AreEqual(5f, closest2.z, Epsilon);
        }

        [Test]
        public void TryGetClosestPointsHandlesParallelLines()
        {
            Line3D line1 = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Line3D line2 = new(new Vector3(0f, 5f, 0f), new Vector3(10f, 5f, 0f));

            bool result = line1.TryGetClosestPoints(
                line2,
                out Vector3 closest1,
                out Vector3 closest2
            );

            Assert.IsFalse(result);
        }

        [Test]
        public void TryGetClosestPointsHandlesIntersectingLines()
        {
            Line3D line1 = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f));
            Line3D line2 = new(new Vector3(0f, 10f, 0f), new Vector3(10f, 0f, 10f));

            bool result = line1.TryGetClosestPoints(
                line2,
                out Vector3 closest1,
                out Vector3 closest2
            );

            Assert.IsTrue(result);
            float distance = Vector3.Distance(closest1, closest2);
            Assert.Less(distance, 10f);
        }

        [Test]
        public void DistanceToLineReturnsZeroForIntersectingLines()
        {
            Line3D line1 = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Line3D line2 = new(new Vector3(5f, -5f, 0f), new Vector3(5f, 5f, 0f));

            float distance = line1.DistanceToLine(line2);

            Assert.AreEqual(0f, distance, Epsilon);
        }

        [Test]
        public void DistanceToLineReturnsCorrectDistanceForSkewLines()
        {
            Line3D line1 = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Line3D line2 = new(new Vector3(5f, 5f, 5f), new Vector3(5f, -5f, 5f));

            float distance = line1.DistanceToLine(line2);

            Assert.AreEqual(5f, distance, Epsilon);
        }

        [Test]
        public void EqualsReturnsTrueForSameLine()
        {
            Line3D line1 = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f));
            Line3D line2 = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f));
            Assert.IsTrue(line1.Equals(line2));
        }

        [Test]
        public void EqualsReturnsFalseForDifferentLines()
        {
            Line3D line1 = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f));
            Line3D line2 = new(new Vector3(0f, 0f, 0f), new Vector3(5f, 5f, 5f));
            Assert.IsFalse(line1.Equals(line2));
        }

        [Test]
        public void EqualsReturnsFalseForReversedLine()
        {
            Line3D line1 = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f));
            Line3D line2 = new(new Vector3(10f, 10f, 10f), new Vector3(0f, 0f, 0f));
            Assert.IsFalse(line1.Equals(line2));
        }

        [Test]
        public void EqualsIsExactSoNearlyIdenticalEndpointsDiffer()
        {
            Line3D line1 = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f));
            Line3D line2 = new(new Vector3(1e-6f, 0f, 0f), new Vector3(10f, 10f, 10f));

            /*
                Unity's Vector3 == is an approximate comparison and Vector3.GetHashCode is not, so
                this pair used to compare equal and hash apart.
            */
            Assert.IsFalse(line1.Equals(line2));
            Assert.IsTrue(line1 != line2);
            Assert.IsTrue(line1.ApproximatelyEquals(line2, 1e-4f));
        }

        [Test]
        public void ApproximatelyEqualsComparesBothEndpointsInOrder()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f));

            Assert.IsTrue(
                line.ApproximatelyEquals(
                    new Line3D(new Vector3(0.001f, 0f, 0f), new Vector3(10f, 10f, 10f)),
                    0.01f
                )
            );
            Assert.IsFalse(
                line.ApproximatelyEquals(
                    new Line3D(new Vector3(0f, 0f, 0f), new Vector3(10.1f, 10f, 10f)),
                    0.01f
                )
            );
            Assert.IsFalse(
                line.ApproximatelyEquals(
                    new Line3D(new Vector3(10f, 10f, 10f), new Vector3(0f, 0f, 0f)),
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
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f));

            Assert.IsFalse(line.ApproximatelyEquals(line, tolerance));
        }

        [Test]
        public void OperatorEqualsWorks()
        {
            Line3D line1 = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f));
            Line3D line2 = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f));
            Line3D line3 = new(new Vector3(1f, 1f, 1f), new Vector3(10f, 10f, 10f));

            Assert.IsTrue(line1 == line2);
            Assert.IsFalse(line1 == line3);
        }

        [Test]
        public void OperatorNotEqualsWorks()
        {
            Line3D line1 = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f));
            Line3D line2 = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f));
            Line3D line3 = new(new Vector3(1f, 1f, 1f), new Vector3(10f, 10f, 10f));

            Assert.IsFalse(line1 != line2);
            Assert.IsTrue(line1 != line3);
        }

        [Test]
        public void GetHashCodeReturnsSameValueForEqualLines()
        {
            Line3D line1 = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f));
            Line3D line2 = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f));
            Assert.AreEqual(line1.GetHashCode(), line2.GetHashCode());
        }

        [Test]
        public void GetHashCodeReturnsDifferentValuesForDifferentLines()
        {
            Line3D line1 = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f));
            Line3D line2 = new(new Vector3(0f, 0f, 0f), new Vector3(5f, 5f, 5f));
            Assert.AreNotEqual(line1.GetHashCode(), line2.GetHashCode());
        }

        [Test]
        public void ToStringReturnsFormattedString()
        {
            Line3D line = new(new Vector3(1f, 2f, 3f), new Vector3(4f, 5f, 6f));
            string result = line.ToString();
            Assert.IsTrue(result.Contains("Line3D"));
            Assert.IsTrue(result.Contains("from"));
            Assert.IsTrue(result.Contains("to"));
        }

        [Test]
        public void ZeroLengthLineHasZeroLength()
        {
            Line3D line = new(new Vector3(5f, 5f, 5f), new Vector3(5f, 5f, 5f));
            Assert.AreEqual(0f, line.Length, Epsilon);
            Assert.AreEqual(0f, line.LengthSquared, Epsilon);
        }

        [Test]
        public void ZeroLengthLineDirectionIsZero()
        {
            Line3D line = new(new Vector3(5f, 5f, 5f), new Vector3(5f, 5f, 5f));
            Assert.AreEqual(Vector3.zero, line.Direction);
        }

        [Test]
        public void VerySmallLineSegmentWorks()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(0.0001f, 0.0001f, 0.0001f));
            Assert.Greater(line.Length, 0f);
        }

        [Test]
        public void VeryLargeLineSegmentWorks()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(1000000f, 1000000f, 1000000f));
            Assert.Greater(line.Length, 0f);
        }

        [Test]
        public void NegativeCoordinatesWork()
        {
            Line3D line = new(new Vector3(-10f, -10f, -10f), new Vector3(10f, 10f, 10f));
            Assert.AreEqual(
                new Vector3(0f, 0f, 0f),
                line.ClosestPointOnLine(new Vector3(0f, 0f, 0f))
            );
        }

        [Test]
        public void AxisAlignedLinesWork()
        {
            Line3D xAxis = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Line3D yAxis = new(new Vector3(0f, 0f, 0f), new Vector3(0f, 10f, 0f));
            Line3D zAxis = new(new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 10f));

            Assert.AreEqual(10f, xAxis.Length, Epsilon);
            Assert.AreEqual(10f, yAxis.Length, Epsilon);
            Assert.AreEqual(10f, zAxis.Length, Epsilon);
        }

        [Test]
        public void PerpendicularLinesHaveCorrectDistance()
        {
            Line3D line1 = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Line3D line2 = new(new Vector3(5f, 5f, 0f), new Vector3(5f, 5f, 10f));

            float distance = line1.DistanceToLine(line2);

            Assert.AreEqual(5f, distance, Epsilon);
        }

        [Test]
        public void ClosestPointsOnPerpendicularLines()
        {
            Line3D line1 = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Line3D line2 = new(new Vector3(5f, 0f, 5f), new Vector3(5f, 10f, 5f));

            Assert.IsTrue(
                line1.TryGetClosestPoints(line2, out Vector3 closest1, out Vector3 closest2)
            );

            Assert.AreEqual(5f, closest1.x, Epsilon);
            Assert.AreEqual(0f, closest1.y, Epsilon);
            Assert.AreEqual(0f, closest1.z, Epsilon);

            Assert.AreEqual(5f, closest2.x, Epsilon);
            Assert.AreEqual(0f, closest2.y, Epsilon);
            Assert.AreEqual(5f, closest2.z, Epsilon);
        }

        [Test]
        public void IntersectsSphereReturnsTrueForLineThroughSphere()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Sphere sphere = new(new Vector3(5f, 0f, 0f), 2f);
            Assert.IsTrue(line.Intersects(sphere));
        }

        [Test]
        public void IntersectsSphereReturnsTrueForLineTouchingSphere()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Sphere sphere = new(new Vector3(5f, 3f, 0f), 3f);
            Assert.IsTrue(line.Intersects(sphere));
        }

        [Test]
        public void IntersectsSphereReturnsFalseForNonIntersectingSphere()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Sphere sphere = new(new Vector3(5f, 5f, 5f), 2f);
            Assert.IsFalse(line.Intersects(sphere));
        }

        [Test]
        public void IntersectsSphereReturnsTrueForSphereContainingEndpoint()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Sphere sphere = new(new Vector3(0f, 0f, 0f), 1f);
            Assert.IsTrue(line.Intersects(sphere));
        }

        [Test]
        public void IntersectsSphereReturnsFalseForSphereNearLineSegmentButNotTouching()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(5f, 0f, 0f));
            Sphere sphere = new(new Vector3(10f, 0f, 0f), 2f);
            Assert.IsFalse(line.Intersects(sphere));
        }

        [Test]
        public void DistanceToSphereReturnsZeroForIntersectingSphere()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Sphere sphere = new(new Vector3(5f, 0f, 0f), 2f);
            Assert.AreEqual(0f, line.DistanceToSphere(sphere), Epsilon);
        }

        [Test]
        public void DistanceToSphereReturnsCorrectDistanceForNonIntersectingSphere()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Sphere sphere = new(new Vector3(5f, 5f, 0f), 2f);
            Assert.AreEqual(3f, line.DistanceToSphere(sphere), Epsilon);
        }

        [Test]
        public void DistanceToSphereReturnsZeroForSphereTouchingLine()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Sphere sphere = new(new Vector3(5f, 3f, 0f), 3f);
            Assert.AreEqual(0f, line.DistanceToSphere(sphere), Epsilon);
        }

        [Test]
        public void IntersectsBoundingBox3DReturnsTrueForLineThroughBounds()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f));
            BoundingBox3D bounds = new(new Vector3(4f, 4f, 4f), new Vector3(6f, 6f, 6f));
            Assert.IsTrue(line.Intersects(bounds));
        }

        [Test]
        public void IntersectsBoundingBox3DReturnsTrueForLineStartingInsideBounds()
        {
            Line3D line = new(new Vector3(5f, 5f, 5f), new Vector3(10f, 10f, 10f));
            BoundingBox3D bounds = new(new Vector3(0f, 0f, 0f), new Vector3(6f, 6f, 6f));
            Assert.IsTrue(line.Intersects(bounds));
        }

        [Test]
        public void IntersectsBoundingBox3DReturnsTrueForLineEndingInsideBounds()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(5f, 5f, 5f));
            BoundingBox3D bounds = new(new Vector3(4f, 4f, 4f), new Vector3(10f, 10f, 10f));
            Assert.IsTrue(line.Intersects(bounds));
        }

        [Test]
        public void IntersectsBoundingBox3DReturnsFalseForNonIntersectingBounds()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(1f, 1f, 1f));
            BoundingBox3D bounds = new(new Vector3(5f, 5f, 5f), new Vector3(10f, 10f, 10f));
            Assert.IsFalse(line.Intersects(bounds));
        }

        [Test]
        public void IntersectsBoundingBox3DReturnsFalseForEmptyBounds()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f));
            BoundingBox3D bounds = BoundingBox3D.Empty;
            Assert.IsFalse(line.Intersects(bounds));
        }

        [Test]
        public void DistanceToBoundsReturnsZeroForIntersectingBounds()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f));
            BoundingBox3D bounds = new(new Vector3(4f, 4f, 4f), new Vector3(6f, 6f, 6f));
            Assert.AreEqual(0f, line.DistanceToBounds(bounds), Epsilon);
        }

        [Test]
        public void DistanceToBoundsReturnsCorrectDistanceForNonIntersectingBounds()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f));
            BoundingBox3D bounds = new(new Vector3(5f, 0f, 0f), new Vector3(10f, 5f, 5f));
            Assert.AreEqual(4f, line.DistanceToBounds(bounds), Epsilon);
        }

        [Test]
        public void DistanceToBoundsReturnsInfinityForEmptyBounds()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f));
            BoundingBox3D bounds = BoundingBox3D.Empty;
            Assert.IsTrue(float.IsPositiveInfinity(line.DistanceToBounds(bounds)));
        }

        [Test]
        public void ClosestPointOnBoundsReturnsPointOnLineNearBounds()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            BoundingBox3D bounds = new(new Vector3(5f, 5f, 5f), new Vector3(10f, 10f, 10f));
            Vector3 closest = line.ClosestPointOnBounds(bounds);
            Assert.IsTrue(0f <= closest.x && closest.x <= 10f);
            Assert.AreEqual(0f, closest.y, Epsilon);
            Assert.AreEqual(0f, closest.z, Epsilon);
        }

        [Test]
        public void ClosestPointOnBoundsReturnsFromForEmptyBounds()
        {
            Line3D line = new(new Vector3(1f, 2f, 3f), new Vector3(10f, 10f, 10f));
            BoundingBox3D bounds = BoundingBox3D.Empty;
            Vector3 closest = line.ClosestPointOnBounds(bounds);
            Assert.AreEqual(line.from, closest);
        }

        [Test]
        public void DistanceSquaredToPointReturnsZeroForPointOnLine()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Vector3 point = new(5f, 0f, 0f);
            Assert.AreEqual(0f, line.DistanceSquaredToPoint(point), Epsilon);
        }

        [Test]
        public void DistanceSquaredToPointReturnsSquaredDistance()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Vector3 point = new(5f, 3f, 4f);
            Assert.AreEqual(25f, line.DistanceSquaredToPoint(point), Epsilon);
        }

        [Test]
        public void DistanceSquaredToPointMatchesDistanceSquared()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Vector3 point = new(5f, 3f, 4f);
            float distance = line.DistanceToPoint(point);
            float distanceSquared = line.DistanceSquaredToPoint(point);
            Assert.AreEqual(distance * distance, distanceSquared, Epsilon);
        }

        [Test]
        public void SphereIntersectionHandlesVerticalLine()
        {
            Line3D line = new(new Vector3(5f, 0f, 5f), new Vector3(5f, 10f, 5f));
            Sphere sphere = new(new Vector3(5f, 5f, 5f), 2f);
            Assert.IsTrue(line.Intersects(sphere));
        }

        [Test]
        public void SphereIntersectionHandlesDiagonalLine()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f));
            Sphere sphere = new(new Vector3(5f, 5f, 5f), 1f);
            Assert.IsTrue(line.Intersects(sphere));
        }

        [Test]
        public void SphereIntersectionHandlesSmallSphere()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Sphere sphere = new(new Vector3(5f, 0.001f, 0f), 0.01f);
            Assert.IsTrue(line.Intersects(sphere));
        }

        [Test]
        public void SphereIntersectionHandlesLargeSphere()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f));
            Sphere sphere = new(new Vector3(0f, 0f, 0f), 1000f);
            Assert.IsTrue(line.Intersects(sphere));
        }

        [Test]
        public void BoundingBoxIntersectionHandlesAxisAlignedLine()
        {
            Line3D line = new(new Vector3(0f, 5f, 5f), new Vector3(10f, 5f, 5f));
            BoundingBox3D bounds = new(new Vector3(4f, 4f, 4f), new Vector3(6f, 6f, 6f));
            Assert.IsTrue(line.Intersects(bounds));
        }

        [Test]
        public void BoundingBoxIntersectionHandlesDiagonalLine()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(10f, 10f, 10f));
            BoundingBox3D bounds = new(new Vector3(4f, 4f, 4f), new Vector3(6f, 6f, 6f));
            Assert.IsTrue(line.Intersects(bounds));
        }

        [Test]
        public void DistanceToSphereHandlesSphereFarFromLineSegment()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f));
            Sphere sphere = new(new Vector3(10f, 10f, 10f), 2f);
            Vector3 closestPointOnLine = new(1f, 0f, 0f);
            float distanceToCenter = Vector3.Distance(closestPointOnLine, sphere.center);
            float expected = distanceToCenter - sphere.radius;
            Assert.AreEqual(expected, line.DistanceToSphere(sphere), 0.01f);
        }

        [Test]
        public void DistanceToBoundsHandlesBoundsFarFromLineSegment()
        {
            Line3D line = new(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f));
            BoundingBox3D bounds = new(new Vector3(10f, 10f, 10f), new Vector3(15f, 15f, 15f));
            Assert.Greater(line.DistanceToBounds(bounds), 15f);
        }
    }
}
