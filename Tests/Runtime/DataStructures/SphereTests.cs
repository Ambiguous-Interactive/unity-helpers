// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Math;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SphereTests
    {
        // Contains(point) tests
        [Test]
        public void ContainsPointInsideReturnsTrue()
        {
            Sphere s = new(Vector3.zero, 2f);
            Assert.IsTrue(s.Contains(new Vector3(1f, 0.5f, 0.5f)));
        }

        [Test]
        public void ContainsPointOnSurfaceReturnsTrue()
        {
            Sphere s = new(Vector3.zero, 1f);
            Assert.IsTrue(s.Contains(new Vector3(1f, 0f, 0f)));
        }

        [Test]
        public void ContainsPointOutsideReturnsFalse()
        {
            Sphere s = new(Vector3.zero, 1f);
            Assert.IsFalse(s.Contains(new Vector3(1.0001f, 0f, 0f)));
        }

        [Test]
        public void ContainsZeroRadiusOnlyCenterIsTrue()
        {
            Vector3 center = new(3.2f, -5f, 7.7f);
            Sphere s = new(center, 0f);
            Assert.IsTrue(s.Contains(center));
            Assert.IsFalse(s.Contains(center + Vector3.right * 1e-6f));
        }

        [Test]
        public void ContainsNegativeRadiusBehavesLikePositiveDueToSquaring()
        {
            Vector3 center = new(0.1f, 0.2f, 0.3f);
            Sphere sNeg = new(center, -2f);
            Sphere sPos = new(center, 2f);
            Assert.AreEqual(
                sPos.Contains(center + new Vector3(2f, 0f, 0f)),
                sNeg.Contains(center + new Vector3(2f, 0f, 0f))
            );
            Assert.AreEqual(
                sPos.Contains(center + new Vector3(2.0001f, 0f, 0f)),
                sNeg.Contains(center + new Vector3(2.0001f, 0f, 0f))
            );
        }

        // Intersects(Bounds) tests
        [Test]
        public void IntersectsBoundsFarOutsideReturnsFalse()
        {
            Sphere s = new(Vector3.zero, 1f);
            Bounds b = new(new Vector3(5f, 5f, 5f), new Vector3(2f, 2f, 2f));
            Assert.IsFalse(s.Intersects(BoundingBox3D.FromClosedBounds(b)));
        }

        [Test]
        public void IntersectsBoundsTouchingFaceReturnsTrue()
        {
            // Sphere at origin radius 1. Bounds begins at x=1, spans across y,z = [-1,1]
            Sphere s = new(Vector3.zero, 1f);
            Bounds b = new(center: new Vector3(1.5f, 0f, 0f), size: new Vector3(1f, 2f, 2f)); // min.x = 1
            Assert.IsTrue(s.Intersects(BoundingBox3D.FromClosedBounds(b)));
        }

        [Test]
        public void IntersectsBoundsTouchingEdgeReturnsTrue()
        {
            // Use a thin slab that aligns so closest point is exactly (1,1,0) on a radius sqrt(2) sphere
            // For unit sphere, we instead create bounds such that closest point is (1,0,0) along an "edge" (zero thickness in z)
            Sphere s = new(Vector3.zero, 1f);
            Bounds b = new(center: new Vector3(1.5f, 0f, 0f), size: new Vector3(1f, 2f, 0f)); // an edge along Y at x in [1,2]
            Assert.IsTrue(s.Intersects(BoundingBox3D.FromClosedBounds(b)));
        }

        [Test]
        public void IntersectsBoundsTouchingCornerReturnsTrue()
        {
            Sphere s = new(Vector3.zero, 1f);
            // A point-like bounds whose center is on the sphere surface
            Bounds b = new(center: new Vector3(1f, 0f, 0f), size: Vector3.zero);
            Assert.IsTrue(s.Intersects(BoundingBox3D.FromClosedBounds(b)));
        }

        [Test]
        public void IntersectsBoundsFullyInsideSphereReturnsTrue()
        {
            Sphere s = new(Vector3.zero, 5f);
            Bounds b = new(center: new Vector3(0.5f, 0.5f, 0.5f), size: new Vector3(1f, 1f, 1f));
            Assert.IsTrue(s.Intersects(BoundingBox3D.FromClosedBounds(b)));
        }

        [Test]
        public void IntersectsSphereFullyInsideBoundsReturnsTrue()
        {
            Sphere s = new(Vector3.zero, 1f);
            Bounds b = new(center: Vector3.zero, size: new Vector3(10f, 10f, 10f));
            Assert.IsTrue(s.Intersects(BoundingBox3D.FromClosedBounds(b)));
        }

        [Test]
        public void IntersectsTouchingDueToToleranceIsTrue()
        {
            // Place bounds so its face is at distance 1 + 1e-7 (just inside tolerance 1e-6)
            float radius = 1f;
            Sphere s = new(Vector3.zero, radius);
            float epsilon = 1e-7f;
            Bounds b = new(
                center: new Vector3(1f + epsilon + 0.5f, 0f, 0f),
                size: new Vector3(1f, 2f, 2f)
            ); // min.x = 1 + epsilon
            Assert.IsTrue(s.Intersects(BoundingBox3D.FromClosedBounds(b)));
        }

        [Test]
        public void IntersectsOutsideBeyondToleranceIsFalse()
        {
            float radius = 1f;
            Sphere s = new(Vector3.zero, radius);
            float epsilon = 2e-6f; // larger than tolerance used in Sphere
            Bounds b = new(
                center: new Vector3(1f + epsilon + 0.5f, 0f, 0f),
                size: new Vector3(1f, 2f, 2f)
            ); // min.x = 1 + epsilon
            Assert.IsFalse(s.Intersects(BoundingBox3D.FromClosedBounds(b)));
        }

        // Overlaps(Bounds) tests: in implementation this means bounds are fully contained in the sphere
        [Test]
        public void OverlapsBoundsFullyInsideSphereReturnsTrue()
        {
            Sphere s = new(Vector3.zero, 3f);
            Bounds b = new(center: new Vector3(0.5f, 0.5f, 0.5f), size: new Vector3(1f, 1f, 1f));
            Assert.IsTrue(s.Overlaps(BoundingBox3D.FromClosedBounds(b)));
        }

        [Test]
        public void OverlapsBoundsPartiallyInsideReturnsFalse()
        {
            Sphere s = new(Vector3.zero, 1f);
            Bounds b = new(center: new Vector3(0.8f, 0f, 0f), size: new Vector3(1f, 1f, 1f));
            Assert.IsFalse(s.Overlaps(BoundingBox3D.FromClosedBounds(b)));
        }

        [Test]
        public void OverlapsBoundsCompletelyOutsideReturnsFalse()
        {
            Sphere s = new(Vector3.zero, 1f);
            Bounds b = new(center: new Vector3(5f, 0f, 0f), size: new Vector3(1f, 1f, 1f));
            Assert.IsFalse(s.Overlaps(BoundingBox3D.FromClosedBounds(b)));
        }

        [Test]
        public void OverlapsZeroSizeBoundsAtCenterReturnsTrue()
        {
            Vector3 center = new(2f, -1f, 3f);
            Sphere s = new(center, 0f);
            Bounds b = new(center: center, size: Vector3.zero);
            Assert.IsTrue(s.Overlaps(BoundingBox3D.FromClosedBounds(b)));
        }

        [Test]
        public void OverlapsBoundsOnSphereSurfaceReturnsFalse()
        {
            Sphere s = new(Vector3.zero, 5f);
            // Bounds with corner at distance exactly 5 from origin
            Bounds b = new(center: new Vector3(3f, 3f, 3f), size: new Vector3(2f, 2f, 2f));
            // Max corner is at (4, 4, 4), distance = sqrt(48) ≈ 6.93, outside sphere
            Assert.IsFalse(s.Overlaps(BoundingBox3D.FromClosedBounds(b)));
        }

        [Test]
        public void OverlapsBoundsWithOnlyOneCornerOutsideReturnsFalse()
        {
            Sphere s = new(Vector3.zero, 2f);
            Bounds b = new(center: new Vector3(0.5f, 0.5f, 1.8f), size: new Vector3(1f, 1f, 1f));
            // One corner at (1, 1, 2.3), distance ≈ 2.64, outside radius 2
            Assert.IsFalse(s.Overlaps(BoundingBox3D.FromClosedBounds(b)));
        }

        [Test]
        public void OverlapsEmptyBoundsReturnsTrue()
        {
            Sphere s = new(Vector3.zero, 1f);
            Assert.IsTrue(s.Overlaps(BoundingBox3D.Empty));
        }

        [Test]
        public void OverlapsBoundsAtOriginWithSphereCenteredElsewhereReturnsExpectedResult()
        {
            Sphere s = new(new Vector3(10f, 10f, 10f), 2f);
            Bounds b = new(center: Vector3.zero, size: new Vector3(1f, 1f, 1f));
            // Farthest corner from (10,10,10) is very far away
            Assert.IsFalse(s.Overlaps(BoundingBox3D.FromClosedBounds(b)));
        }

        [Test]
        public void OverlapsLargeBoundsContainingSphereReturnsFalse()
        {
            Sphere s = new(Vector3.zero, 1f);
            Bounds b = new(center: Vector3.zero, size: new Vector3(100f, 100f, 100f));
            // Bounds corners are far outside the sphere
            Assert.IsFalse(s.Overlaps(BoundingBox3D.FromClosedBounds(b)));
        }

        [Test]
        public void OverlapsNegativeRadiusBehavesLikePositiveDueToSquaring()
        {
            Vector3 center = new(1f, 2f, 3f);
            Sphere sNeg = new(center, -3f);
            Sphere sPos = new(center, 3f);
            Bounds b = new(center: center, size: new Vector3(2f, 2f, 2f));
            Assert.AreEqual(
                sPos.Overlaps(BoundingBox3D.FromClosedBounds(b)),
                sNeg.Overlaps(BoundingBox3D.FromClosedBounds(b))
            );
        }

        [Test]
        public void OverlapsBoundsExactlyFittingInsideSphereReturnsTrue()
        {
            Sphere s = new(Vector3.zero, 5f);
            // Cube centered at origin with corners at (±2, ±2, ±2), farthest distance = sqrt(12) ≈ 3.46
            Bounds b = new(center: Vector3.zero, size: new Vector3(4f, 4f, 4f));
            Assert.IsTrue(s.Overlaps(BoundingBox3D.FromClosedBounds(b)));
        }

        [Test]
        public void OverlapsBoundsCenteredAwayFromSphereReturnsExpectedResult()
        {
            Sphere s = new(new Vector3(5f, 0f, 0f), 3f);
            Bounds b = new(center: new Vector3(7f, 0f, 0f), size: new Vector3(2f, 2f, 2f));
            // Closest corner to sphere center: (6, -1, -1), distance ≈ sqrt(1 + 1 + 1) ≈ 1.73
            // Farthest corner: (8, 1, 1), distance ≈ sqrt(9 + 1 + 1) ≈ 3.32, outside radius 3
            Assert.IsFalse(s.Overlaps(BoundingBox3D.FromClosedBounds(b)));
        }

        [Test]
        public void OverlapsAsymmetricBoundsReturnsExpectedResult()
        {
            Sphere s = new(Vector3.zero, 10f);
            Bounds b = new(center: new Vector3(2f, 3f, 4f), size: new Vector3(6f, 2f, 4f));
            // Max corner at (5, 4, 6), distance = sqrt(25 + 16 + 36) = sqrt(77) ≈ 8.77
            Assert.IsTrue(s.Overlaps(BoundingBox3D.FromClosedBounds(b)));
        }

        [Test]
        public void OverlapsZeroRadiusSphereWithNonZeroBoundsReturnsFalse()
        {
            Vector3 center = new(1f, 2f, 3f);
            Sphere s = new(center, 0f);
            Bounds b = new(center: center, size: new Vector3(1f, 1f, 1f));
            // Even small bounds won't fit in zero-radius sphere
            Assert.IsFalse(s.Overlaps(BoundingBox3D.FromClosedBounds(b)));
        }

        // Contains(point) edge cases
        [Test]
        public void ContainsVeryClosePointDueToFloatingPointReturnsExpectedResult()
        {
            Sphere s = new(Vector3.zero, 1f);
            Vector3 almostOnSurface = new(1f - 1e-7f, 0f, 0f);
            Assert.IsTrue(s.Contains(almostOnSurface));
        }

        [Test]
        public void ContainsPointAtMaxFloatDistanceReturnsFalse()
        {
            Sphere s = new(Vector3.zero, 1f);
            Assert.IsFalse(s.Contains(new Vector3(float.MaxValue, 0f, 0f)));
        }

        // Intersects edge cases
        [Test]
        public void IntersectsVerySmallBoundsInsideSphereReturnsTrue()
        {
            Sphere s = new(Vector3.zero, 5f);
            Bounds b = new(center: new Vector3(1f, 1f, 1f), size: new Vector3(0.01f, 0.01f, 0.01f));
            Assert.IsTrue(s.Intersects(BoundingBox3D.FromClosedBounds(b)));
        }

        [Test]
        public void IntersectsEmptyBoundsReturnsTrue()
        {
            Sphere s = new(Vector3.zero, 1f);
            Assert.IsTrue(s.Intersects(BoundingBox3D.Empty));
        }

        [Test]
        public void IntersectsNegativeRadiusBehavesLikePositive()
        {
            Vector3 center = new(2f, 3f, 4f);
            Sphere sNeg = new(center, -2f);
            Sphere sPos = new(center, 2f);
            Bounds b = new(center: new Vector3(3f, 3f, 4f), size: new Vector3(1f, 1f, 1f));
            Assert.AreEqual(
                sPos.Intersects(BoundingBox3D.FromClosedBounds(b)),
                sNeg.Intersects(BoundingBox3D.FromClosedBounds(b))
            );
        }

        // Intersects(Sphere) tests
        [Test]
        public void IntersectsSphereReturnsTrueForOverlappingSpheres()
        {
            Sphere sphere1 = new(Vector3.zero, 5f);
            Sphere sphere2 = new(new Vector3(3f, 0f, 0f), 3f);

            Assert.IsTrue(sphere1.Intersects(sphere2));
            Assert.IsTrue(sphere2.Intersects(sphere1));
        }

        [Test]
        public void IntersectsSphereReturnsTrueForTouchingSpheres()
        {
            Sphere sphere1 = new(Vector3.zero, 5f);
            Sphere sphere2 = new(new Vector3(10f, 0f, 0f), 5f);

            Assert.IsTrue(sphere1.Intersects(sphere2));
        }

        [Test]
        public void IntersectsSphereReturnsFalseForNonOverlappingSpheres()
        {
            Sphere sphere1 = new(Vector3.zero, 2f);
            Sphere sphere2 = new(new Vector3(10f, 10f, 10f), 3f);

            Assert.IsFalse(sphere1.Intersects(sphere2));
        }

        [Test]
        public void IntersectsSphereHandlesIdenticalSpheres()
        {
            Sphere sphere1 = new(new Vector3(5f, 5f, 5f), 3f);
            Sphere sphere2 = new(new Vector3(5f, 5f, 5f), 3f);

            Assert.IsTrue(sphere1.Intersects(sphere2));
        }

        [Test]
        public void IntersectsSphereHandlesOneSphereInsideAnother()
        {
            Sphere bigSphere = new(Vector3.zero, 10f);
            Sphere smallSphere = new(new Vector3(2f, 2f, 2f), 1f);

            Assert.IsTrue(bigSphere.Intersects(smallSphere));
            Assert.IsTrue(smallSphere.Intersects(bigSphere));
        }

        [Test]
        public void IntersectsSphereHandlesZeroRadiusSpheres()
        {
            Sphere sphere1 = new(Vector3.zero, 0f);
            Sphere sphere2 = new(Vector3.zero, 5f);

            Assert.IsTrue(sphere1.Intersects(sphere2));
            Assert.IsTrue(sphere2.Intersects(sphere1));
        }

        // Equality tests
        [Test]
        public void EqualsReturnsTrueForIdenticalSpheres()
        {
            Sphere sphere1 = new(new Vector3(5f, 10f, 15f), 3f);
            Sphere sphere2 = new(new Vector3(5f, 10f, 15f), 3f);

            Assert.IsTrue(sphere1.Equals(sphere2));
            Assert.IsTrue(sphere1 == sphere2);
        }

        [Test]
        public void EqualsReturnsFalseForDifferentCenters()
        {
            Sphere sphere1 = new(new Vector3(5f, 10f, 15f), 3f);
            Sphere sphere2 = new(new Vector3(6f, 10f, 15f), 3f);

            Assert.IsFalse(sphere1.Equals(sphere2));
            Assert.IsTrue(sphere1 != sphere2);
        }

        [Test]
        public void EqualsReturnsFalseForDifferentRadii()
        {
            Sphere sphere1 = new(new Vector3(5f, 10f, 15f), 3f);
            Sphere sphere2 = new(new Vector3(5f, 10f, 15f), 4f);

            Assert.IsFalse(sphere1.Equals(sphere2));
            Assert.IsTrue(sphere1 != sphere2);
        }

        [Test]
        public void EqualsIsExactSoNearlyIdenticalSpheresDiffer()
        {
            Sphere sphere1 = new(new Vector3(5f, 10f, 15f), 3f);
            Sphere sphere2 = new(new Vector3(5f, 10f, 15f), 3f + 1e-6f);

            /*
                These radii used to compare equal through Mathf.Approximately while hashing on their
                exact bits, so the pair landed in different buckets of the same set.
            */
            Assert.IsFalse(sphere1.Equals(sphere2));
            Assert.IsTrue(sphere1 != sphere2);
            Assert.IsTrue(sphere1.ApproximatelyEquals(sphere2, 1e-5f));
        }

        [Test]
        public void ApproximatelyEqualsComparesCentresAndRadius()
        {
            Sphere sphere = new(new Vector3(5f, 10f, 15f), 3f);

            Assert.IsTrue(
                sphere.ApproximatelyEquals(new Sphere(new Vector3(5f, 10f, 15.001f), 3f), 0.01f)
            );
            Assert.IsFalse(
                sphere.ApproximatelyEquals(new Sphere(new Vector3(5f, 10f, 15.1f), 3f), 0.01f)
            );
            Assert.IsFalse(
                sphere.ApproximatelyEquals(new Sphere(new Vector3(5f, 10f, 15f), 3.1f), 0.01f)
            );
        }

        [Test]
        [TestCase(-1f, TestName = "Tolerance.Negative.ReturnsFalse")]
        [TestCase(float.NaN, TestName = "Tolerance.NotANumber.ReturnsFalse")]
        [TestCase(float.PositiveInfinity, TestName = "Tolerance.Infinite.ReturnsFalse")]
        public void ApproximatelyEqualsRefusesAnInvalidTolerance(float tolerance)
        {
            Sphere sphere = new(new Vector3(5f, 10f, 15f), 3f);

            Assert.IsFalse(sphere.ApproximatelyEquals(sphere, tolerance));
        }

        [Test]
        public void GetHashCodeReturnsSameValueForEqualSpheres()
        {
            Sphere sphere1 = new(new Vector3(5f, 10f, 15f), 3f);
            Sphere sphere2 = new(new Vector3(5f, 10f, 15f), 3f);

            Assert.AreEqual(sphere1.GetHashCode(), sphere2.GetHashCode());
        }

        [Test]
        public void GetHashCodeReturnsDifferentValuesForDifferentSpheres()
        {
            Sphere sphere1 = new(new Vector3(5f, 10f, 15f), 3f);
            Sphere sphere2 = new(new Vector3(6f, 10f, 15f), 3f);

            // While not guaranteed, different spheres should typically have different hash codes
            Assert.AreNotEqual(sphere1.GetHashCode(), sphere2.GetHashCode());
        }

        [Test]
        public void ToStringReturnsValidString()
        {
            Sphere sphere = new(new Vector3(5f, 10f, 15f), 3f);
            string str = sphere.ToString();

            Assert.IsTrue(str != null, "Sphere.ToString should not return null");
            Assert.IsTrue(str.Contains("Sphere"));
            Assert.IsTrue(
                str.Contains("5") || str.Contains("10") || str.Contains("15") || str.Contains("3")
            );
        }

        [Test]
        public void SpheresCanBeUsedInHashSet()
        {
            System.Collections.Generic.HashSet<Sphere> spheres = new();

            Sphere sphere1 = new(new Vector3(5f, 10f, 15f), 3f);
            Sphere sphere2 = new(new Vector3(5f, 10f, 15f), 3f);
            Sphere sphere3 = new(new Vector3(6f, 10f, 15f), 3f);

            Assert.IsTrue(spheres.Add(sphere1));
            Assert.IsFalse(spheres.Add(sphere2)); // Should be considered duplicate
            Assert.IsTrue(spheres.Add(sphere3));

            Assert.AreEqual(2, spheres.Count);
        }

        // Unity Bounds overload tests
        [Test]
        public void IntersectsUnityBoundsReturnsTrueForOverlap()
        {
            Sphere sphere = new(Vector3.zero, 5f);
            Bounds bounds = new(Vector3.zero, new Vector3(4f, 4f, 4f));

            Assert.IsTrue(sphere.Intersects(bounds));
        }

        [Test]
        public void IntersectsUnityBoundsReturnsFalseForNonOverlap()
        {
            Sphere sphere = new(Vector3.zero, 2f);
            Bounds bounds = new(new Vector3(10f, 10f, 10f), new Vector3(2f, 2f, 2f));

            Assert.IsFalse(sphere.Intersects(bounds));
        }

        [Test]
        public void IntersectsUnityBoundsMatchesBoundingBox3DResult()
        {
            Sphere sphere = new(new Vector3(5f, 3f, 2f), 4f);
            Bounds bounds = new(new Vector3(7f, 3f, 2f), new Vector3(2f, 2f, 2f));

            bool boundsResult = sphere.Intersects(bounds);
            bool boundingBox3DResult = sphere.Intersects(BoundingBox3D.FromClosedBounds(bounds));

            Assert.AreEqual(boundingBox3DResult, boundsResult);
        }

        [Test]
        public void OverlapsUnityBoundsReturnsTrueForFullContainment()
        {
            Sphere sphere = new(Vector3.zero, 10f);
            Bounds bounds = new(Vector3.zero, new Vector3(2f, 2f, 2f));

            Assert.IsTrue(sphere.Overlaps(bounds));
        }

        [Test]
        public void OverlapsUnityBoundsReturnsFalseForPartialOverlap()
        {
            Sphere sphere = new(Vector3.zero, 5f);
            Bounds bounds = new(new Vector3(4f, 4f, 4f), new Vector3(4f, 4f, 4f));

            Assert.IsFalse(sphere.Overlaps(bounds));
        }

        [Test]
        public void OverlapsUnityBoundsMatchesBoundingBox3DResult()
        {
            Sphere sphere = new(new Vector3(5f, 5f, 5f), 5f);
            Bounds bounds = new(new Vector3(3f, 3f, 3f), new Vector3(2f, 2f, 2f));

            bool boundsResult = sphere.Overlaps(bounds);
            bool boundingBox3DResult = sphere.Overlaps(BoundingBox3D.FromClosedBounds(bounds));

            Assert.AreEqual(boundingBox3DResult, boundsResult);
        }

        [Test]
        public void IntersectsLineReturnsTrueForLineThroughSphere()
        {
            Sphere sphere = new(Vector3.zero, 5f);
            Line3D line = new(new Vector3(-10f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Assert.IsTrue(sphere.Intersects(line));
        }

        [Test]
        public void IntersectsLineReturnsTrueForLineTouchingSphere()
        {
            Sphere sphere = new(Vector3.zero, 5f);
            Line3D line = new(new Vector3(-10f, 5f, 0f), new Vector3(10f, 5f, 0f));
            Assert.IsTrue(sphere.Intersects(line));
        }

        [Test]
        public void IntersectsLineReturnsFalseForLineNotIntersectingSphere()
        {
            Sphere sphere = new(Vector3.zero, 5f);
            Line3D line = new(new Vector3(-10f, 10f, 0f), new Vector3(10f, 10f, 0f));
            Assert.IsFalse(sphere.Intersects(line));
        }

        [Test]
        public void IntersectsLineReturnsTrueForLineEndpointInsideSphere()
        {
            Sphere sphere = new(Vector3.zero, 5f);
            Line3D line = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Assert.IsTrue(sphere.Intersects(line));
        }

        [Test]
        public void IntersectsLineReturnsFalseForLineSegmentNotReachingSphere()
        {
            Sphere sphere = new(Vector3.zero, 2f);
            Line3D line = new(new Vector3(10f, 0f, 0f), new Vector3(20f, 0f, 0f));
            Assert.IsFalse(sphere.Intersects(line));
        }

        [Test]
        public void IntersectsLineHandlesVerticalLine()
        {
            Sphere sphere = new(new Vector3(5f, 5f, 5f), 3f);
            Line3D verticalIntersecting = new(new Vector3(5f, 0f, 5f), new Vector3(5f, 10f, 5f));
            Line3D verticalNotIntersecting = new(
                new Vector3(10f, 0f, 5f),
                new Vector3(10f, 10f, 5f)
            );
            Assert.IsTrue(sphere.Intersects(verticalIntersecting));
            Assert.IsFalse(sphere.Intersects(verticalNotIntersecting));
        }

        [Test]
        public void IntersectsLineHandlesDiagonalLine3D()
        {
            Sphere sphere = new(new Vector3(5f, 5f, 5f), 3f);
            Line3D diagonal = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Assert.IsTrue(sphere.Intersects(diagonal));
        }

        [Test]
        public void IntersectsLineHandlesSmallSphere()
        {
            Sphere sphere = new(new Vector3(5f, 0f, 0f), 0.1f);
            Line3D line = new(Vector3.zero, new Vector3(10f, 0f, 0f));
            Assert.IsTrue(sphere.Intersects(line));
        }

        [Test]
        public void IntersectsLineHandlesLineInAllThreePlanes()
        {
            Sphere sphere = new(Vector3.zero, 5f);
            Line3D lineXY = new(new Vector3(-10f, 3f, 0f), new Vector3(10f, 3f, 0f));
            Line3D lineYZ = new(new Vector3(0f, -10f, 3f), new Vector3(0f, 10f, 3f));
            Line3D lineXZ = new(new Vector3(3f, 0f, -10f), new Vector3(3f, 0f, 10f));
            Assert.IsTrue(sphere.Intersects(lineXY));
            Assert.IsTrue(sphere.Intersects(lineYZ));
            Assert.IsTrue(sphere.Intersects(lineXZ));
        }

        [Test]
        public void DistanceToLineReturnsZeroForIntersectingLine()
        {
            Sphere sphere = new(Vector3.zero, 5f);
            Line3D line = new(new Vector3(-10f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Assert.AreEqual(0f, sphere.DistanceToLine(line), 0.0001f);
        }

        [Test]
        public void DistanceToLineReturnsCorrectDistanceForNonIntersectingLine()
        {
            Sphere sphere = new(Vector3.zero, 2f);
            Line3D line = new(new Vector3(-10f, 5f, 0f), new Vector3(10f, 5f, 0f));
            Assert.AreEqual(3f, sphere.DistanceToLine(line), 0.0001f);
        }

        [Test]
        public void DistanceToLineReturnsZeroForTouchingLine()
        {
            Sphere sphere = new(Vector3.zero, 5f);
            Line3D line = new(new Vector3(-10f, 5f, 0f), new Vector3(10f, 5f, 0f));
            Assert.AreEqual(0f, sphere.DistanceToLine(line), 0.0001f);
        }

        [Test]
        public void DistanceToLineHandlesLineSegmentNearSphere()
        {
            Sphere sphere = new(Vector3.zero, 2f);
            Line3D line = new(new Vector3(5f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Assert.AreEqual(3f, sphere.DistanceToLine(line), 0.0001f);
        }

        [Test]
        public void ClosestPointOnLineReturnsPointOnLineSegment()
        {
            Sphere sphere = new(new Vector3(5f, 5f, 5f), 2f);
            Line3D line = new(Vector3.zero, new Vector3(10f, 0f, 0f));
            Vector3 closest = sphere.ClosestPointOnLine(line);
            Assert.AreEqual(5f, closest.x, 0.0001f);
            Assert.AreEqual(0f, closest.y, 0.0001f);
            Assert.AreEqual(0f, closest.z, 0.0001f);
        }

        [Test]
        public void ClosestPointOnLineReturnsCenterProjection()
        {
            Sphere sphere = new(new Vector3(5f, 3f, 2f), 1f);
            Line3D line = new(Vector3.zero, new Vector3(10f, 0f, 0f));
            Vector3 closest = sphere.ClosestPointOnLine(line);
            Assert.AreEqual(5f, closest.x, 0.0001f);
            Assert.AreEqual(0f, closest.y, 0.0001f);
            Assert.AreEqual(0f, closest.z, 0.0001f);
        }

        [Test]
        public void ClosestPointOnLineHandlesEndpointClamping()
        {
            Sphere sphere = new(new Vector3(15f, 5f, 5f), 2f);
            Line3D line = new(Vector3.zero, new Vector3(10f, 0f, 0f));
            Vector3 closest = sphere.ClosestPointOnLine(line);
            Assert.AreEqual(10f, closest.x, 0.0001f);
            Assert.AreEqual(0f, closest.y, 0.0001f);
            Assert.AreEqual(0f, closest.z, 0.0001f);
        }

        [Test]
        public void ClosestPointOnLineHandlesVerticalLine3D()
        {
            Sphere sphere = new(new Vector3(5f, 5f, 8f), 2f);
            Line3D line = new(new Vector3(8f, 0f, 8f), new Vector3(8f, 10f, 8f));
            Vector3 closest = sphere.ClosestPointOnLine(line);
            Assert.AreEqual(8f, closest.x, 0.0001f);
            Assert.AreEqual(5f, closest.y, 0.0001f);
            Assert.AreEqual(8f, closest.z, 0.0001f);
        }

        [Test]
        public void ClosestPointOnLineHandlesDiagonalLine3D()
        {
            Sphere sphere = new(new Vector3(0f, 5f, 0f), 1f);
            Line3D line = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Vector3 closest = sphere.ClosestPointOnLine(line);
            float expectedCoord = 5f / 3f;
            Assert.AreEqual(expectedCoord, closest.x, 0.01f);
            Assert.AreEqual(expectedCoord, closest.y, 0.01f);
            Assert.AreEqual(expectedCoord, closest.z, 0.01f);
        }

        [Test]
        public void LineIntersectionHandlesZeroRadiusSphere()
        {
            Sphere sphere = new(new Vector3(5f, 0f, 0f), 0f);
            Line3D line = new(Vector3.zero, new Vector3(10f, 0f, 0f));
            Assert.IsTrue(sphere.Intersects(line));
        }

        [Test]
        public void LineIntersectionHandlesZeroLengthLine()
        {
            Sphere sphere = new(new Vector3(5f, 5f, 5f), 3f);
            Line3D pointInsideSphere = new(new Vector3(5f, 5f, 5f), new Vector3(5f, 5f, 5f));
            Line3D pointOutsideSphere = new(new Vector3(10f, 10f, 10f), new Vector3(10f, 10f, 10f));
            Assert.IsTrue(sphere.Intersects(pointInsideSphere));
            Assert.IsFalse(sphere.Intersects(pointOutsideSphere));
        }

        [Test]
        public void LineDistanceHandlesParallelLineSegment3D()
        {
            Sphere sphere = new(new Vector3(5f, 5f, 5f), 2f);
            Line3D line = new(Vector3.zero, new Vector3(2f, 0f, 0f));
            float distance = sphere.DistanceToLine(line);
            Vector3 closestOnLine = new(2f, 0f, 0f);
            float expectedDistance = Vector3.Distance(sphere.center, closestOnLine) - sphere.radius;
            Assert.AreEqual(expectedDistance, distance, 0.01f);
        }

        [Test]
        public void LineIntersectionHandlesSphereAtOrigin()
        {
            Sphere sphere = new(Vector3.zero, 5f);
            Line3D lineX = new(new Vector3(-10f, 0f, 0f), new Vector3(10f, 0f, 0f));
            Line3D lineY = new(new Vector3(0f, -10f, 0f), new Vector3(0f, 10f, 0f));
            Line3D lineZ = new(new Vector3(0f, 0f, -10f), new Vector3(0f, 0f, 10f));
            Line3D diagonal = new(new Vector3(-10f, -10f, -10f), new Vector3(10f, 10f, 10f));
            Assert.IsTrue(sphere.Intersects(lineX));
            Assert.IsTrue(sphere.Intersects(lineY));
            Assert.IsTrue(sphere.Intersects(lineZ));
            Assert.IsTrue(sphere.Intersects(diagonal));
        }

        [Test]
        public void LineDistanceHandlesNegativeCoordinates3D()
        {
            Sphere sphere = new(new Vector3(-5f, -5f, -5f), 2f);
            Line3D line = new(new Vector3(-10f, 0f, 0f), new Vector3(0f, 0f, 0f));
            float distance = sphere.DistanceToLine(line);
            Assert.Greater(distance, 0f);
        }

        [Test]
        public void ClosestPointOnLineHandlesSphereCenterOnLine()
        {
            Sphere sphere = new(new Vector3(5f, 0f, 0f), 3f);
            Line3D line = new(Vector3.zero, new Vector3(10f, 0f, 0f));
            Vector3 closest = sphere.ClosestPointOnLine(line);
            Assert.AreEqual(sphere.center, closest);
        }

        [Test]
        public void LineIntersectionConsistentWithLineSphereIntersection()
        {
            Sphere sphere = new(new Vector3(5f, 5f, 5f), 3f);
            Line3D line = new(new Vector3(0f, 5f, 5f), new Vector3(10f, 5f, 5f));
            Assert.AreEqual(line.Intersects(sphere), sphere.Intersects(line));
        }

        [Test]
        public void LineDistanceConsistentWithLineSphereDistance()
        {
            Sphere sphere = new(new Vector3(5f, 5f, 5f), 3f);
            Line3D line = new(Vector3.zero, new Vector3(10f, 0f, 0f));
            Assert.AreEqual(line.DistanceToSphere(sphere), sphere.DistanceToLine(line), 0.0001f);
        }

        [Test]
        public void IntersectsLineHandlesSkewLines()
        {
            Sphere sphere = new(new Vector3(5f, 5f, 5f), 3f);
            Line3D skewLine = new(new Vector3(0f, 0f, 10f), new Vector3(10f, 0f, 10f));
            bool intersects = sphere.Intersects(skewLine);
            float distanceToCenter = skewLine.DistanceToPoint(sphere.center);
            Assert.AreEqual(distanceToCenter <= sphere.radius, intersects);
        }

        [Test]
        public void ClosestPointOnLineHandlesLineInAllOctants()
        {
            Sphere sphere = new(new Vector3(5f, 5f, 5f), 2f);
            Line3D line1 = new(new Vector3(-10f, -10f, -10f), new Vector3(-5f, -5f, -5f));
            Line3D line2 = new(new Vector3(15f, 15f, 15f), new Vector3(20f, 20f, 20f));
            Vector3 closest1 = sphere.ClosestPointOnLine(line1);
            Vector3 closest2 = sphere.ClosestPointOnLine(line2);
            Assert.AreEqual(new Vector3(-5f, -5f, -5f), closest1);
            Assert.AreEqual(new Vector3(15f, 15f, 15f), closest2);
        }

        [Test]
        public void LineDistanceHandlesVerySmallSphere()
        {
            Sphere sphere = new(new Vector3(5f, 5f, 5f), 0.001f);
            Line3D line = new(Vector3.zero, new Vector3(10f, 0f, 0f));
            float distance = sphere.DistanceToLine(line);
            Assert.Greater(distance, 0f);
        }

        [Test]
        public void LineDistanceHandlesVeryLargeSphere()
        {
            Sphere sphere = new(Vector3.zero, 1000f);
            Line3D line = new(new Vector3(500f, 500f, 500f), new Vector3(600f, 600f, 600f));
            Assert.AreEqual(0f, sphere.DistanceToLine(line), 0.01f);
        }
    }
}
