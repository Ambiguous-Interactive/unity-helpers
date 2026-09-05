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
    public sealed class BoundingBox3DTests
    {
        [Test]
        public void ConstructorWithValidBoundsCreatesBox()
        {
            Vector3 min = new(0f, 0f, 0f);
            Vector3 max = new(10f, 10f, 10f);
            BoundingBox3D box = new(min, max);

            Assert.AreEqual(min, box.min);
        }

        [Test]
        public void ConstructorWithEqualMinMaxCreatesValidBox()
        {
            Vector3 point = new(5f, 5f, 5f);
            BoundingBox3D box = new(point, point);

            Assert.AreEqual(point, box.min);
            Assert.IsFalse(box.IsEmpty);
        }

        [Test]
        public void ConstructorWithInvalidBoundsThrowsException()
        {
            Vector3 min = new(10f, 10f, 10f);
            Vector3 max = new(0f, 0f, 0f);

            Assert.Throws<System.ArgumentException>(() => new BoundingBox3D(min, max));
        }

        [Test]
        public void ConstructorWithPartiallyInvalidBoundsThrowsException()
        {
            Vector3 min = new(0f, 10f, 0f);
            Vector3 max = new(10f, 0f, 10f);

            Assert.Throws<System.ArgumentException>(() => new BoundingBox3D(min, max));
        }

        [Test]
        public void CenterReturnsCorrectMidpoint()
        {
            Vector3 min = new(0f, 0f, 0f);
            Vector3 max = new(10f, 10f, 10f);
            BoundingBox3D box = new(min, max);

            Vector3 expectedCenter = new(5f, 5f, 5f);
            Assert.AreEqual(expectedCenter.x, box.Center.x, 0.0001f);
            Assert.AreEqual(expectedCenter.y, box.Center.y, 0.0001f);
            Assert.AreEqual(expectedCenter.z, box.Center.z, 0.0001f);
        }

        [Test]
        public void SizeReturnsCorrectDimensions()
        {
            Vector3 min = new(-5f, -5f, -5f);
            Vector3 max = new(5f, 5f, 5f);
            BoundingBox3D box = new(min, max);

            Vector3 expectedSize = new(10f, 10f, 10f);
            Assert.AreEqual(expectedSize.x, box.Size.x, 0.0001f);
            Assert.AreEqual(expectedSize.y, box.Size.y, 0.0001f);
            Assert.AreEqual(expectedSize.z, box.Size.z, 0.0001f);
        }

        [Test]
        public void IsEmptyReturnsTrueForDefaultBox()
        {
            BoundingBox3D box = default;
            Assert.IsTrue(box.IsEmpty);
        }

        [Test]
        public void IsEmptyReturnsFalseForValidBox()
        {
            BoundingBox3D box = new(Vector3.zero, Vector3.one);
            Assert.IsFalse(box.IsEmpty);
        }

        [Test]
        public void EmptyPropertyReturnsEmptyBox()
        {
            BoundingBox3D empty = BoundingBox3D.Empty;
            Assert.IsTrue(empty.IsEmpty);
        }

        [Test]
        public void FromCenterAndSizeCreatesCorrectBox()
        {
            Vector3 center = new(5f, 5f, 5f);
            Vector3 size = new(10f, 10f, 10f);
            BoundingBox3D box = BoundingBox3D.FromCenterAndSize(center, size);

            Assert.AreEqual(new Vector3(0f, 0f, 0f), box.min);
            Assert.AreEqual(center.x, box.Center.x, 0.0001f);
            Assert.AreEqual(center.y, box.Center.y, 0.0001f);
            Assert.AreEqual(center.z, box.Center.z, 0.0001f);
        }

        [Test]
        public void FromCenterAndSizeWithZeroSizeCreatesPoint()
        {
            Vector3 center = new(5f, 5f, 5f);
            BoundingBox3D box = BoundingBox3D.FromCenterAndSize(center, Vector3.zero);

            Assert.IsFalse(box.IsEmpty);
            Assert.AreEqual(center, box.min);
        }

        [Test]
        public void FromClosedBoundsCreatesCorrectBox()
        {
            Bounds bounds = new(new Vector3(5f, 5f, 5f), new Vector3(10f, 10f, 10f));
            BoundingBox3D box = BoundingBox3D.FromClosedBounds(bounds);

            Assert.AreEqual(bounds.min, box.min);
        }

        [Test]
        public void FromPointCreatesValidBox()
        {
            Vector3 point = new(3f, 4f, 5f);
            BoundingBox3D box = BoundingBox3D.FromPoint(point);

            Assert.AreEqual(point, box.min);
            Assert.IsFalse(box.IsEmpty);
        }

        [Test]
        public void ContainsPointInsideReturnsTrue()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Vector3 point = new(5f, 5f, 5f);

            Assert.IsTrue(box.Contains(point));
        }

        [Test]
        public void ContainsPointOnMinBoundaryReturnsTrue()
        {
            BoundingBox3D box = new(Vector3.zero, Vector3.one);
            Assert.IsTrue(box.Contains(Vector3.zero));
        }

        [Test]
        public void ContainsPointOnMaxBoundaryReturnsFalse()
        {
            BoundingBox3D box = new(Vector3.zero, Vector3.one);
            Assert.IsFalse(box.Contains(Vector3.one));
        }

        [Test]
        public void ContainsPointOutsideReturnsFalse()
        {
            BoundingBox3D box = new(Vector3.zero, Vector3.one);
            Vector3 point = new(2f, 2f, 2f);

            Assert.IsFalse(box.Contains(point));
        }

        [Test]
        public void ContainsPointJustBelowMinReturnsFalse()
        {
            BoundingBox3D box = new(Vector3.one, new Vector3(10f, 10f, 10f));
            Vector3 point = new(0.999f, 5f, 5f);

            Assert.IsFalse(box.Contains(point));
        }

        [Test]
        public void ContainsBoxCompletelyInsideReturnsTrue()
        {
            BoundingBox3D outer = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            BoundingBox3D inner = new(new Vector3(2f, 2f, 2f), new Vector3(8f, 8f, 8f));

            Assert.IsTrue(outer.Contains(inner));
        }

        [Test]
        public void ContainsBoxPartiallyOutsideReturnsFalse()
        {
            BoundingBox3D box1 = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            BoundingBox3D box2 = new(new Vector3(5f, 5f, 5f), new Vector3(15f, 15f, 15f));

            Assert.IsFalse(box1.Contains(box2));
        }

        [Test]
        public void ContainsBoxCompletelyOutsideReturnsFalse()
        {
            BoundingBox3D box1 = new(Vector3.zero, Vector3.one);
            BoundingBox3D box2 = new(new Vector3(10f, 10f, 10f), new Vector3(20f, 20f, 20f));

            Assert.IsFalse(box1.Contains(box2));
        }

        [Test]
        public void ContainsIdenticalBoxReturnsTrue()
        {
            BoundingBox3D box1 = new(Vector3.zero, Vector3.one);
            BoundingBox3D box2 = new(Vector3.zero, Vector3.one);

            Assert.IsTrue(box1.Contains(box2));
        }

        [Test]
        public void ContainsEmptyBoxReturnsFalse()
        {
            BoundingBox3D box = new(Vector3.zero, Vector3.one);
            Assert.IsFalse(box.Contains(BoundingBox3D.Empty));
        }

        [Test]
        public void IntersectsOverlappingBoxesReturnsTrue()
        {
            BoundingBox3D box1 = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            BoundingBox3D box2 = new(new Vector3(5f, 5f, 5f), new Vector3(15f, 15f, 15f));

            Assert.IsTrue(box1.Intersects(box2));
            Assert.IsTrue(box2.Intersects(box1));
        }

        [Test]
        public void IntersectsTouchingBoxesReturnsFalse()
        {
            BoundingBox3D box1 = new(Vector3.zero, Vector3.one);
            BoundingBox3D box2 = new(Vector3.one, new Vector3(2f, 2f, 2f));

            Assert.IsFalse(box1.Intersects(box2));
        }

        [Test]
        public void IntersectsNonOverlappingBoxesReturnsFalse()
        {
            BoundingBox3D box1 = new(Vector3.zero, Vector3.one);
            BoundingBox3D box2 = new(new Vector3(2f, 2f, 2f), new Vector3(3f, 3f, 3f));

            Assert.IsFalse(box1.Intersects(box2));
        }

        [Test]
        public void IntersectsBoxInsideAnotherReturnsTrue()
        {
            BoundingBox3D outer = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            BoundingBox3D inner = new(new Vector3(2f, 2f, 2f), new Vector3(8f, 8f, 8f));

            Assert.IsTrue(outer.Intersects(inner));
            Assert.IsTrue(inner.Intersects(outer));
        }

        [Test]
        public void IntersectsOnSingleAxisReturnsFalse()
        {
            BoundingBox3D box1 = new(Vector3.zero, Vector3.one);
            BoundingBox3D box2 = new(new Vector3(0.5f, 2f, 0.5f), new Vector3(1.5f, 3f, 1.5f));

            Assert.IsFalse(box1.Intersects(box2));
        }

        [Test]
        public void IntersectsEmptyBoxReturnsFalse()
        {
            BoundingBox3D box = new(Vector3.zero, Vector3.one);
            Assert.IsFalse(box.Intersects(BoundingBox3D.Empty));
        }

        [Test]
        public void ExpandToIncludePointInsideReturnsUnchanged()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Vector3 point = new(5f, 5f, 5f);
            BoundingBox3D expanded = box.ExpandToInclude(point);

            Assert.AreEqual(box.min, expanded.min);
        }

        [Test]
        public void ExpandToIncludePointOutsideExpandsBox()
        {
            BoundingBox3D box = new(Vector3.zero, Vector3.one);
            Vector3 point = new(5f, 5f, 5f);
            BoundingBox3D expanded = box.ExpandToInclude(point);

            Assert.IsTrue(expanded.Contains(point));
            Assert.AreEqual(Vector3.zero, expanded.min);
        }

        [Test]
        public void ExpandToIncludePointBelowMinExpandsMin()
        {
            BoundingBox3D box = new(Vector3.one, new Vector3(10f, 10f, 10f));
            Vector3 point = Vector3.zero;
            BoundingBox3D expanded = box.ExpandToInclude(point);

            Assert.AreEqual(Vector3.zero, expanded.min);
        }

        [Test]
        public void ExpandToIncludePointAboveMaxExpandsMax()
        {
            BoundingBox3D box = new(Vector3.zero, Vector3.one);
            Vector3 point = new(5f, 5f, 5f);
            BoundingBox3D expanded = box.ExpandToInclude(point);

            Assert.IsTrue(expanded.Contains(point));
        }

        [Test]
        public void ExpandToIncludeBoxCompletelyInsideReturnsUnchanged()
        {
            BoundingBox3D outer = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            BoundingBox3D inner = new(new Vector3(2f, 2f, 2f), new Vector3(8f, 8f, 8f));
            BoundingBox3D expanded = outer.ExpandToInclude(inner);

            Assert.AreEqual(outer.min, expanded.min);
        }

        [Test]
        public void ExpandToIncludeBoxPartiallyOutsideExpands()
        {
            BoundingBox3D box1 = new(Vector3.zero, new Vector3(5f, 5f, 5f));
            BoundingBox3D box2 = new(new Vector3(3f, 3f, 3f), new Vector3(10f, 10f, 10f));
            BoundingBox3D expanded = box1.ExpandToInclude(box2);

            Assert.AreEqual(Vector3.zero, expanded.min);
            Assert.IsTrue(expanded.Contains(box2));
        }

        [Test]
        public void ExpandToIncludeEmptyBoxReturnsUnchanged()
        {
            BoundingBox3D box = new(Vector3.zero, Vector3.one);
            BoundingBox3D expanded = box.ExpandToInclude(BoundingBox3D.Empty);

            Assert.AreEqual(box.min, expanded.min);
        }

        [Test]
        public void EmptyBoxExpandToIncludeBoxReturnsOtherBox()
        {
            BoundingBox3D empty = BoundingBox3D.Empty;
            BoundingBox3D box = new(Vector3.zero, Vector3.one);
            BoundingBox3D result = empty.ExpandToInclude(box);

            Assert.AreEqual(box.min, result.min);
        }

        [Test]
        public void EnsureMinimumSizeWithSmallerBoxExpands()
        {
            BoundingBox3D box = new(new Vector3(5f, 5f, 5f), new Vector3(6f, 6f, 6f));
            BoundingBox3D expanded = box.EnsureMinimumSize(5f);

            Vector3 size = expanded.Size;
            Assert.GreaterOrEqual(size.x, 5f);
            Assert.GreaterOrEqual(size.y, 5f);
            Assert.GreaterOrEqual(size.z, 5f);
        }

        [Test]
        public void EnsureMinimumSizeWithLargerBoxReturnsUnchanged()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            BoundingBox3D result = box.EnsureMinimumSize(5f);

            Assert.AreEqual(box.min, result.min);
        }

        [Test]
        public void EnsureMinimumSizeWithZeroMinimumReturnsUnchanged()
        {
            BoundingBox3D box = new(Vector3.zero, Vector3.one);
            BoundingBox3D result = box.EnsureMinimumSize(0f);

            Assert.AreEqual(box.min, result.min);
        }

        [Test]
        public void EnsureMinimumSizeWithNegativeMinimumReturnsUnchanged()
        {
            BoundingBox3D box = new(Vector3.zero, Vector3.one);
            BoundingBox3D result = box.EnsureMinimumSize(-5f);

            Assert.AreEqual(box.min, result.min);
        }

        [Test]
        public void EnsureMinimumSizeOnEmptyBoxReturnsEmpty()
        {
            BoundingBox3D empty = BoundingBox3D.Empty;
            BoundingBox3D result = empty.EnsureMinimumSize(5f);

            Assert.IsTrue(result.IsEmpty);
        }

        [Test]
        public void EnsureMinimumSizeExpandsSymmetrically()
        {
            Vector3 center = new(5f, 5f, 5f);
            BoundingBox3D box = BoundingBox3D.FromCenterAndSize(center, Vector3.one);
            BoundingBox3D expanded = box.EnsureMinimumSize(5f);

            Vector3 expandedCenter = expanded.Center;
            Assert.AreEqual(center.x, expandedCenter.x, 0.01f);
            Assert.AreEqual(center.y, expandedCenter.y, 0.01f);
            Assert.AreEqual(center.z, expandedCenter.z, 0.01f);
        }

        [Test]
        public void ClosestPointInsideReturnsPoint()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Vector3 point = new(5f, 5f, 5f);
            Vector3 closest = box.ClosestPoint(point);

            Assert.AreEqual(point, closest);
        }

        [Test]
        public void ClosestPointOutsideReturnsBoundaryPoint()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Vector3 point = new(15f, 15f, 15f);
            Vector3 closest = box.ClosestPoint(point);

            Assert.IsTrue(
                box.Contains(closest)
                    || Mathf.Approximately(closest.x, box.max.x)
                    || Mathf.Approximately(closest.y, box.max.y)
                    || Mathf.Approximately(closest.z, box.max.z)
            );
        }

        [Test]
        public void ClosestPointOnFaceReturnsFacePoint()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Vector3 point = new(15f, 5f, 5f);
            Vector3 closest = box.ClosestPoint(point);

            Assert.AreEqual(5f, closest.y);
            Assert.AreEqual(5f, closest.z);
        }

        [Test]
        public void ClosestPointOnEdgeReturnsEdgePoint()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Vector3 point = new(15f, 15f, 5f);
            Vector3 closest = box.ClosestPoint(point);

            Assert.AreEqual(5f, closest.z);
        }

        [Test]
        public void ClosestPointOnCornerReturnsCorner()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Vector3 point = new(-5f, -5f, -5f);
            Vector3 closest = box.ClosestPoint(point);

            Assert.AreEqual(Vector3.zero, closest);
        }

        [Test]
        public void ClosestPointOnMaxBoundaryReturnsBoundary()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Vector3 point = new(20f, 5f, 5f);
            Vector3 closest = box.ClosestPoint(point);

            Assert.AreEqual(box.max.x, closest.x);
        }

        [Test]
        public void DistanceSquaredToPointInsideReturnsZero()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Vector3 point = new(5f, 5f, 5f);
            float distSq = box.DistanceSquaredTo(point);

            Assert.AreEqual(0f, distSq, 0.0001f);
        }

        [Test]
        public void DistanceSquaredToPointOutsideReturnsCorrectDistance()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Vector3 point = new(13f, 10f, 10f);
            float distSq = box.DistanceSquaredTo(point);

            Assert.AreEqual(9f, distSq, 0.0001f);
        }

        [Test]
        public void DistanceSquaredToPointOnBoundaryReturnsZero()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Vector3 point = Vector3.zero;
            float distSq = box.DistanceSquaredTo(point);

            Assert.AreEqual(0f, distSq, 0.0001f);
        }

        [Test]
        public void ToBoundsCreatesCorrectBounds()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Bounds bounds = box.ToBounds();

            Assert.AreEqual(box.Size.x, bounds.size.x, 0.0001f);
            Assert.AreEqual(box.Size.y, bounds.size.y, 0.0001f);
            Assert.AreEqual(box.Size.z, bounds.size.z, 0.0001f);
        }

        [Test]
        public void ToBoundsPreservesCenter()
        {
            Vector3 center = new(5f, 5f, 5f);
            BoundingBox3D box = BoundingBox3D.FromCenterAndSize(center, new Vector3(10f, 10f, 10f));
            Bounds bounds = box.ToBounds();

            Assert.AreEqual(center.x, bounds.center.x, 0.01f);
            Assert.AreEqual(center.y, bounds.center.y, 0.01f);
            Assert.AreEqual(center.z, bounds.center.z, 0.01f);
        }

        [Test]
        public void HalfOpenSemanticsMaxBoundaryExcluded()
        {
            BoundingBox3D box = new(Vector3.zero, Vector3.one);

            Assert.IsTrue(box.Contains(Vector3.zero));
            Assert.IsFalse(box.Contains(Vector3.one));
            Assert.IsTrue(box.Contains(new Vector3(0.999f, 0.999f, 0.999f)));
        }

        [Test]
        public void NegativeCoordinatesWork()
        {
            BoundingBox3D box = new(new Vector3(-10f, -10f, -10f), Vector3.zero);

            Assert.IsTrue(box.Contains(new Vector3(-5f, -5f, -5f)));
            Assert.IsFalse(box.Contains(new Vector3(1f, 1f, 1f)));
        }

        [Test]
        public void VerySmallBoxWorks()
        {
            float epsilon = 0.0001f;
            BoundingBox3D box = new(Vector3.zero, new Vector3(epsilon, epsilon, epsilon));

            Assert.IsFalse(box.IsEmpty);
            Assert.IsTrue(box.Contains(new Vector3(epsilon / 2f, epsilon / 2f, epsilon / 2f)));
        }

        [Test]
        public void VeryLargeBoxWorks()
        {
            float large = 1000000f;
            BoundingBox3D box = new(
                new Vector3(-large, -large, -large),
                new Vector3(large, large, large)
            );

            Assert.IsTrue(box.Contains(Vector3.zero));
            Assert.IsTrue(box.Contains(new Vector3(large / 2f, large / 2f, large / 2f)));
        }

        [Test]
        public void AsymmetricBoxWorks()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(1f, 10f, 100f));

            Assert.AreEqual(1f, box.Size.x, 0.0001f);
            Assert.AreEqual(10f, box.Size.y, 0.0001f);
            Assert.AreEqual(100f, box.Size.z, 0.0001f);
        }

        [Test]
        public void ExpandToIncludeMultiplePointsWorks()
        {
            BoundingBox3D box = BoundingBox3D.FromPoint(Vector3.zero);
            box = box.ExpandToInclude(new Vector3(10f, 0f, 0f));
            box = box.ExpandToInclude(new Vector3(0f, 10f, 0f));
            box = box.ExpandToInclude(new Vector3(0f, 0f, 10f));

            Assert.IsTrue(box.Contains(Vector3.zero));
            Assert.IsTrue(box.Contains(new Vector3(5f, 5f, 5f)));
        }

        [Test]
        public void IntersectsEdgeCaseTouchingAtSinglePointReturnsFalse()
        {
            BoundingBox3D box1 = new(Vector3.zero, Vector3.one);
            BoundingBox3D box2 = new(Vector3.one, new Vector3(2f, 2f, 2f));

            Assert.IsFalse(box1.Intersects(box2));
        }

        [Test]
        public void ContainsBoxEdgeCaseExactFit()
        {
            BoundingBox3D box1 = new(Vector3.zero, Vector3.one);
            BoundingBox3D box2 = new(Vector3.zero, Vector3.one);

            Assert.IsTrue(box1.Contains(box2));
        }

        [Test]
        public void FromClosedBoundsHandlesPointBounds()
        {
            Bounds bounds = new(Vector3.zero, Vector3.zero);
            BoundingBox3D box = BoundingBox3D.FromClosedBounds(bounds);

            Assert.IsFalse(box.IsEmpty);
        }

        [Test]
        public void ClosestPointHandlesCenterOfBox()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Vector3 center = box.Center;
            Vector3 closest = box.ClosestPoint(center);

            Assert.AreEqual(center, closest);
        }

        [Test]
        public void MultipleExpandOperationsPreserveCorrectness()
        {
            BoundingBox3D box = new(Vector3.zero, Vector3.one);
            Vector3 point1 = new(5f, 5f, 5f);
            Vector3 point2 = new(-5f, -5f, -5f);

            box = box.ExpandToInclude(point1);
            box = box.ExpandToInclude(point2);

            Assert.IsTrue(box.Contains(point1));
            Assert.IsTrue(box.Contains(point2));
            Assert.IsTrue(box.Contains(Vector3.zero));
        }

        [Test]
        public void DistanceSquaredCalculationCorrect()
        {
            BoundingBox3D box = new(Vector3.zero, Vector3.one);
            Vector3 point = new(2f, 2f, 2f);
            float distSq = box.DistanceSquaredTo(point);

            float expected = 3f;
            Assert.AreEqual(expected, distSq, 0.01f);
        }

        [Test]
        public void EnsureMinimumSizeOnOneAxisOnly()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(0.5f, 10f, 10f));
            BoundingBox3D expanded = box.EnsureMinimumSize(5f);

            Assert.GreaterOrEqual(expanded.Size.x, 5f);
            Assert.GreaterOrEqual(expanded.Size.y, 5f);
            Assert.GreaterOrEqual(expanded.Size.z, 5f);
        }

        [Test]
        public void ContainsHandlesMinMaxBoundaryCorrectly()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));

            Assert.IsTrue(box.Contains(box.min));
            Assert.IsFalse(box.Contains(box.max));

            Vector3 justBelowMax = box.max - new Vector3(0.0001f, 0.0001f, 0.0001f);
            Assert.IsTrue(box.Contains(justBelowMax));
        }

        [Test]
        public void IntersectsIsSymmetric()
        {
            BoundingBox3D box1 = new(Vector3.zero, new Vector3(5f, 5f, 5f));
            BoundingBox3D box2 = new(new Vector3(3f, 3f, 3f), new Vector3(8f, 8f, 8f));

            Assert.AreEqual(box1.Intersects(box2), box2.Intersects(box1));
        }

        [Test]
        public void FromPointAndExpandMaintainsPointInclusion()
        {
            Vector3 point = new(3.7f, 2.1f, 9.5f);
            BoundingBox3D box = BoundingBox3D.FromPoint(point);

            Assert.IsTrue(box.Contains(point));
        }

        [Test]
        public void ToBoundsRoundTripPreservesApproximateProperties()
        {
            BoundingBox3D original = new(new Vector3(-5f, -5f, -5f), new Vector3(5f, 5f, 5f));
            Bounds bounds = original.ToBounds();
            BoundingBox3D reconstructed = BoundingBox3D.FromClosedBounds(bounds);

            Assert.AreEqual(original.min.x, reconstructed.min.x, 0.01f);
            Assert.AreEqual(original.min.y, reconstructed.min.y, 0.01f);
            Assert.AreEqual(original.min.z, reconstructed.min.z, 0.01f);
        }

        [Test]
        public void VolumeReturnsCorrectValue()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(2f, 3f, 4f));
            Assert.AreEqual(24f, box.Volume, 0.0001f);
        }

        [Test]
        public void VolumeForEmptyBoxReturnsZeroOrNegative()
        {
            BoundingBox3D empty = BoundingBox3D.Empty;
            Assert.LessOrEqual(empty.Volume, 0f);
        }

        [Test]
        public void EncapsulatePointAliasWorks()
        {
            BoundingBox3D box = new(Vector3.zero, Vector3.one);
            Vector3 point = new(5f, 5f, 5f);
            BoundingBox3D expanded = box.Encapsulate(point);

            Assert.IsTrue(expanded.Contains(point));
            Assert.AreEqual(Vector3.zero, expanded.min);
        }

        [Test]
        public void EncapsulateBoxAliasWorks()
        {
            BoundingBox3D box1 = new(Vector3.zero, new Vector3(5f, 5f, 5f));
            BoundingBox3D box2 = new(new Vector3(3f, 3f, 3f), new Vector3(10f, 10f, 10f));
            BoundingBox3D expanded = box1.Encapsulate(box2);

            Assert.AreEqual(Vector3.zero, expanded.min);
            Assert.IsTrue(expanded.Contains(box2));
        }

        [Test]
        public void UnionAliasWorks()
        {
            BoundingBox3D box1 = new(Vector3.zero, new Vector3(5f, 5f, 5f));
            BoundingBox3D box2 = new(new Vector3(3f, 3f, 3f), new Vector3(10f, 10f, 10f));
            BoundingBox3D union = box1.Union(box2);

            Assert.AreEqual(Vector3.zero, union.min);
            Assert.IsTrue(union.Contains(box1));
            Assert.IsTrue(union.Contains(box2));
        }

        [Test]
        public void IntersectionReturnsCorrectBox()
        {
            BoundingBox3D box1 = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            BoundingBox3D box2 = new(new Vector3(5f, 5f, 5f), new Vector3(15f, 15f, 15f));
            BoundingBox3D? intersection = box1.Intersection(box2);

            Assert.IsTrue(intersection.HasValue);
            Assert.AreEqual(new Vector3(5f, 5f, 5f), intersection.Value.min);
            Assert.AreEqual(new Vector3(10f, 10f, 10f), intersection.Value.max);
        }

        [Test]
        public void IntersectionOfNonOverlappingBoxesReturnsNull()
        {
            BoundingBox3D box1 = new(Vector3.zero, Vector3.one);
            BoundingBox3D box2 = new(new Vector3(2f, 2f, 2f), new Vector3(3f, 3f, 3f));
            BoundingBox3D? intersection = box1.Intersection(box2);

            Assert.IsFalse(intersection.HasValue);
        }

        [Test]
        public void GetCornersReturnsAllEightCorners()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(1f, 2f, 3f));
            Vector3[] corners = new Vector3[8];
            box.GetCorners(corners);

            Assert.AreEqual(new Vector3(0f, 0f, 0f), corners[0]);
            Assert.AreEqual(new Vector3(1f, 0f, 0f), corners[1]);
            Assert.AreEqual(new Vector3(0f, 2f, 0f), corners[2]);
            Assert.AreEqual(new Vector3(1f, 2f, 0f), corners[3]);
            Assert.AreEqual(new Vector3(0f, 0f, 3f), corners[4]);
            Assert.AreEqual(new Vector3(1f, 0f, 3f), corners[5]);
            Assert.AreEqual(new Vector3(0f, 2f, 3f), corners[6]);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), corners[7]);
        }

        [Test]
        public void GetCornersThrowsOnNullArray()
        {
            BoundingBox3D box = new(Vector3.zero, Vector3.one);
            Assert.Throws<System.ArgumentException>(() => box.GetCorners(null));
        }

        [Test]
        public void GetCornersThrowsOnSmallArray()
        {
            BoundingBox3D box = new(Vector3.zero, Vector3.one);
            Vector3[] corners = new Vector3[7];
            Assert.Throws<System.ArgumentException>(() => box.GetCorners(corners));
        }

        [Test]
        public void EqualsReturnsTrueForIdenticalBoxes()
        {
            BoundingBox3D box1 = new(Vector3.zero, Vector3.one);
            BoundingBox3D box2 = new(Vector3.zero, Vector3.one);

            Assert.IsTrue(box1.Equals(box2));
            Assert.IsTrue(box1 == box2);
            Assert.IsFalse(box1 != box2);
        }

        [Test]
        public void EqualsReturnsFalseForDifferentBoxes()
        {
            BoundingBox3D box1 = new(Vector3.zero, Vector3.one);
            BoundingBox3D box2 = new(Vector3.one, new Vector3(2f, 2f, 2f));

            Assert.IsFalse(box1.Equals(box2));
            Assert.IsFalse(box1 == box2);
            Assert.IsTrue(box1 != box2);
        }

        [Test]
        public void HashCodeIsConsistent()
        {
            BoundingBox3D box1 = new(Vector3.zero, Vector3.one);
            BoundingBox3D box2 = new(Vector3.zero, Vector3.one);

            Assert.AreEqual(box1.GetHashCode(), box2.GetHashCode());
        }

        [Test]
        public void ToStringReturnsValidString()
        {
            BoundingBox3D box = new(Vector3.zero, Vector3.one);
            string str = box.ToString();

            Assert.IsTrue(str != null);
            Assert.IsTrue(str.Contains("BoundingBox3D"));
        }

        [Test]
        public void IntersectsLineReturnsTrueForLineThroughBox()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Line3D line = new(new Vector3(-5f, 5f, 5f), new Vector3(15f, 5f, 5f));
            Assert.IsTrue(box.Intersects(line));
        }

        [Test]
        public void IntersectsLineReturnsTrueForLineEndpointInsideBox()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Line3D line = new(new Vector3(5f, 5f, 5f), new Vector3(15f, 15f, 15f));
            Assert.IsTrue(box.Intersects(line));
        }

        [Test]
        public void IntersectsLineReturnsFalseForLineNotIntersectingBox()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Line3D line = new(new Vector3(15f, 15f, 15f), new Vector3(20f, 20f, 20f));
            Assert.IsFalse(box.Intersects(line));
        }

        [Test]
        public void IntersectsLineReturnsFalseForLineSegmentNotReachingBox()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(5f, 5f, 5f));
            Line3D line = new(new Vector3(10f, 0f, 0f), new Vector3(20f, 0f, 0f));
            Assert.IsFalse(box.Intersects(line));
        }

        [Test]
        public void IntersectsLineHandlesLineThroughBoxCenter()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Line3D diagonal = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Assert.IsTrue(box.Intersects(diagonal));
        }

        [Test]
        public void IntersectsLineHandlesLineAlongBoxEdge()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Line3D edgeLine = new(Vector3.zero, new Vector3(10f, 0f, 0f));
            Assert.IsTrue(box.Intersects(edgeLine));
        }

        [Test]
        public void IntersectsLineHandlesLineOnBoxFace()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Line3D faceLine = new(new Vector3(5f, 5f, 0f), new Vector3(8f, 8f, 0f));
            Assert.IsTrue(box.Intersects(faceLine));
        }

        [Test]
        public void IntersectsLineHandlesVerticalLine()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Line3D verticalInside = new(new Vector3(5f, -5f, 5f), new Vector3(5f, 15f, 5f));
            Line3D verticalOutside = new(new Vector3(15f, -5f, 5f), new Vector3(15f, 15f, 5f));
            Assert.IsTrue(box.Intersects(verticalInside));
            Assert.IsFalse(box.Intersects(verticalOutside));
        }

        [Test]
        public void IntersectsLineHandlesEmptyBox()
        {
            BoundingBox3D emptyBox = BoundingBox3D.Empty;
            Line3D line = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Assert.IsFalse(emptyBox.Intersects(line));
        }

        [Test]
        public void IntersectsLineHandlesZeroLengthLine()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Line3D pointInside = new(new Vector3(5f, 5f, 5f), new Vector3(5f, 5f, 5f));
            Line3D pointOutside = new(new Vector3(15f, 15f, 15f), new Vector3(15f, 15f, 15f));
            Assert.IsTrue(box.Intersects(pointInside));
            Assert.IsFalse(box.Intersects(pointOutside));
        }

        [Test]
        public void DistanceToLineReturnsZeroForIntersectingLine()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Line3D line = new(new Vector3(-5f, 5f, 5f), new Vector3(15f, 5f, 5f));
            Assert.AreEqual(0f, box.DistanceToLine(line), 0.0001f);
        }

        [Test]
        public void DistanceToLineReturnsCorrectDistanceForNonIntersectingLine()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(5f, 5f, 5f));
            Line3D line = new(new Vector3(10f, 0f, 0f), new Vector3(20f, 0f, 0f));
            Assert.AreEqual(5f, box.DistanceToLine(line), 0.0001f);
        }

        [Test]
        public void DistanceToLineHandlesLineParallelToBoxFace()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(5f, 5f, 5f));
            Line3D line = new(new Vector3(10f, 2f, 2f), new Vector3(10f, 3f, 3f));
            Assert.Greater(box.DistanceToLine(line), 0f);
        }

        [Test]
        public void DistanceToLineReturnsZeroForLineEndpointInBox()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Line3D line = new(new Vector3(5f, 5f, 5f), new Vector3(20f, 20f, 20f));
            Assert.AreEqual(0f, box.DistanceToLine(line), 0.0001f);
        }

        [Test]
        public void DistanceToLineHandlesEmptyBox()
        {
            BoundingBox3D emptyBox = BoundingBox3D.Empty;
            Line3D line = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Assert.AreEqual(float.PositiveInfinity, emptyBox.DistanceToLine(line), 0.0001f);
        }

        [Test]
        public void ClosestPointOnLineReturnsPointOnLineSegment()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Line3D line = new(new Vector3(15f, 5f, 5f), new Vector3(20f, 5f, 5f));
            Vector3 closest = box.ClosestPointOnLine(line);
            Assert.AreEqual(15f, closest.x, 0.01f);
            Assert.AreEqual(5f, closest.y, 0.01f);
            Assert.AreEqual(5f, closest.z, 0.01f);
        }

        [Test]
        public void ClosestPointOnLineHandlesLineThroughBox()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Line3D line = new(new Vector3(-5f, 5f, 5f), new Vector3(15f, 5f, 5f));
            Vector3 closest = box.ClosestPointOnLine(line);
            Assert.IsTrue(
                box.Contains(closest)
                    || Vector3.Distance(closest, box.ClosestPoint(closest)) < 0.01f
            );
        }

        [Test]
        public void ClosestPointOnLineHandlesEndpointClamping()
        {
            BoundingBox3D box = new(new Vector3(20f, 20f, 20f), new Vector3(30f, 30f, 30f));
            Line3D line = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Vector3 closest = box.ClosestPointOnLine(line);
            Assert.AreEqual(10f, closest.x, 0.01f);
            Assert.AreEqual(10f, closest.y, 0.01f);
            Assert.AreEqual(10f, closest.z, 0.01f);
        }

        [Test]
        public void ClosestPointOnLineHandlesEmptyBox()
        {
            BoundingBox3D emptyBox = BoundingBox3D.Empty;
            Line3D line = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Vector3 closest = emptyBox.ClosestPointOnLine(line);
            Assert.AreEqual(Vector3.zero, closest);
        }

        [Test]
        public void LineIntersectionConsistentWithLineBoundsIntersection()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Line3D line = new(new Vector3(-5f, 5f, 5f), new Vector3(15f, 5f, 5f));
            Assert.AreEqual(line.Intersects(box), box.Intersects(line));
        }

        [Test]
        public void LineDistanceConsistentWithLineBoundsDistance()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Line3D line = new(new Vector3(15f, 5f, 5f), new Vector3(20f, 5f, 5f));
            Assert.AreEqual(line.DistanceToBounds(box), box.DistanceToLine(line), 0.0001f);
        }

        [Test]
        public void IntersectsLineHandlesBoxAtOrigin()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(5f, 5f, 5f));
            Line3D lineX = new(new Vector3(-10f, 2f, 2f), new Vector3(10f, 2f, 2f));
            Line3D lineY = new(new Vector3(2f, -10f, 2f), new Vector3(2f, 10f, 2f));
            Line3D lineZ = new(new Vector3(2f, 2f, -10f), new Vector3(2f, 2f, 10f));
            Assert.IsTrue(box.Intersects(lineX));
            Assert.IsTrue(box.Intersects(lineY));
            Assert.IsTrue(box.Intersects(lineZ));
        }

        [Test]
        public void IntersectsLineHandlesNegativeCoordinates()
        {
            BoundingBox3D box = new(new Vector3(-10f, -10f, -10f), Vector3.zero);
            Line3D line = new(new Vector3(-15f, -5f, -5f), new Vector3(-3f, -5f, -5f));
            Assert.IsTrue(box.Intersects(line));
        }

        [Test]
        public void DistanceToLineHandlesNegativeCoordinates()
        {
            BoundingBox3D box = new(new Vector3(-10f, -10f, -10f), new Vector3(-5f, -5f, -5f));
            Line3D line = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            float distance = box.DistanceToLine(line);
            Assert.Greater(distance, 0f);
        }

        [Test]
        public void IntersectsLineHandlesVerySmallBox()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(0.1f, 0.1f, 0.1f));
            Line3D lineThroughBox = new(
                new Vector3(-1f, 0.05f, 0.05f),
                new Vector3(1f, 0.05f, 0.05f)
            );
            Line3D lineMissingBox = new(new Vector3(-1f, 1f, 1f), new Vector3(1f, 1f, 1f));
            Assert.IsTrue(box.Intersects(lineThroughBox));
            Assert.IsFalse(box.Intersects(lineMissingBox));
        }

        [Test]
        public void IntersectsLineHandlesVeryLargeBox()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(1000f, 1000f, 1000f));
            Line3D lineInsideBox = new(
                new Vector3(100f, 100f, 100f),
                new Vector3(200f, 200f, 200f)
            );
            Assert.IsTrue(box.Intersects(lineInsideBox));
        }

        [Test]
        public void DistanceToLineHandlesLineCloseToCorner()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Line3D line = new(new Vector3(11f, 11f, 11f), new Vector3(20f, 20f, 20f));
            float distance = box.DistanceToLine(line);
            Vector3 boxCorner = new(10f, 10f, 10f);
            Vector3 lineStart = new(11f, 11f, 11f);
            float expectedDistance = Vector3.Distance(boxCorner, lineStart);
            Assert.AreEqual(expectedDistance, distance, 0.1f);
        }

        [Test]
        public void ClosestPointOnLineHandlesDiagonalLine()
        {
            BoundingBox3D box = new(new Vector3(10f, 10f, 10f), new Vector3(20f, 20f, 20f));
            Line3D line = new(Vector3.zero, new Vector3(5f, 5f, 5f));
            Vector3 closest = box.ClosestPointOnLine(line);
            Assert.AreEqual(new Vector3(5f, 5f, 5f), closest);
        }

        [Test]
        public void IntersectsLineHandlesLineGrazingBoxCorner()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));
            Line3D line = new(Vector3.zero, new Vector3(1f, 1f, 1f));
            Assert.IsTrue(box.Intersects(line));
        }

        [Test]
        public void DistanceToLineHandlesParallelLines()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(5f, 5f, 5f));
            Line3D parallelToX = new(new Vector3(10f, 2f, 2f), new Vector3(20f, 2f, 2f));
            Line3D parallelToY = new(new Vector3(2f, 10f, 2f), new Vector3(2f, 20f, 2f));
            Line3D parallelToZ = new(new Vector3(2f, 2f, 10f), new Vector3(2f, 2f, 20f));
            Assert.Greater(box.DistanceToLine(parallelToX), 0f);
            Assert.Greater(box.DistanceToLine(parallelToY), 0f);
            Assert.Greater(box.DistanceToLine(parallelToZ), 0f);
        }

        [Test]
        public void ClosestPointOnLineHandlesMultipleBoxes()
        {
            BoundingBox3D box1 = new(Vector3.zero, new Vector3(5f, 5f, 5f));
            BoundingBox3D box2 = new(new Vector3(10f, 10f, 10f), new Vector3(15f, 15f, 15f));
            Line3D line = new(new Vector3(0f, 0f, 20f), new Vector3(20f, 20f, 20f));
            Vector3 closest1 = box1.ClosestPointOnLine(line);
            Vector3 closest2 = box2.ClosestPointOnLine(line);
            Assert.AreNotEqual(closest1, closest2);
        }

        [Test]
        public void IntersectsLineHandlesZeroLengthAtMaxFace()
        {
            BoundingBox3D box = new(Vector3.zero, new Vector3(10f, 10f, 10f));

            Line3D pointOnMaxFace = new(new Vector3(10f, 5f, 5f), new Vector3(10f, 5f, 5f));
            Assert.IsTrue(box.Intersects(pointOnMaxFace));
        }

        [Test]
        public void IntersectsLineHandlesLineOnMaxFace()
        {
            BoundingBox3D box = new(Vector3.zero, Vector3.one);

            Line3D onFace = new(new Vector3(0.25f, 0.75f, 1f), new Vector3(0.75f, 0.25f, 1f));
            Assert.IsTrue(box.Intersects(onFace));
        }

        [Test]
        public void DistanceToLineMatchesEpsilonOffsetParallel()
        {
            BoundingBox3D box = new(Vector3.zero, Vector3.one);
            float eps = 1e-5f;
            Line3D parallelX = new(
                new Vector3(-1f, 1f + eps, 0.5f),
                new Vector3(2f, 1f + eps, 0.5f)
            );
            float distance = box.DistanceToLine(parallelX);
            Assert.AreEqual(eps, distance, 1e-6f);
        }

        [Test]
        public void IntersectsLineHandlesTinyBox()
        {
            float s = 1e-8f;
            BoundingBox3D tiny = BoundingBox3D.FromCenterAndSize(
                Vector3.zero,
                new Vector3(s, s, s)
            );
            Line3D throughCenter = new(new Vector3(-1f, 0f, 0f), new Vector3(1f, 0f, 0f));
            Assert.IsTrue(tiny.Intersects(throughCenter));
        }

        [Test]
        public void ClosestPointOnLineExactInteriorMinimum()
        {
            BoundingBox3D box = new(Vector3.zero, Vector3.one);
            Vector3 p0 = new(2f, 2f, 2f);
            Vector3 p1 = new(3f, 0f, 0f);
            Line3D seg = new(p0, p1);
            Vector3 corner = Vector3.one;
            Vector3 d = p1 - p0;
            float tStar = Vector3.Dot(corner - p0, d) / d.sqrMagnitude;
            tStar = Mathf.Clamp01(tStar);
            Vector3 expectedOnSegment = p0 + tStar * d;

            Vector3 closest = box.ClosestPointOnLine(seg);
            Assert.AreEqual(expectedOnSegment.x, closest.x, 1e-4f);
            Assert.AreEqual(expectedOnSegment.y, closest.y, 1e-4f);
            Assert.AreEqual(expectedOnSegment.z, closest.z, 1e-4f);

            float expectedDistance = Vector3.Distance(expectedOnSegment, corner);
            float actualDistance = box.DistanceToLine(seg);
            Assert.AreEqual(expectedDistance, actualDistance, 1e-4f);
        }
    }
}
