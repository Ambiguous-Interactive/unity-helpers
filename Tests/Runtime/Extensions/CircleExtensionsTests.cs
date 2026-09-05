// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class CircleExtensionsTests : CommonTestBase
    {
        [Test]
        public void EnumerateAreaReturnsPointsWithinCircle()
        {
            Circle circle = new(new Vector2(0f, 0f), 2f);
            HashSet<FastVector3Int> points = new(circle.EnumerateArea());

            foreach (FastVector3Int point in points)
            {
                Assert.IsTrue(circle.Contains(new Vector2(point.x, point.y)));
            }

            Assert.IsTrue(points.Contains(new FastVector3Int(0, 0, 0)));
            Assert.IsTrue(points.Contains(new FastVector3Int(1, 1, 0)));
            Assert.IsFalse(points.Contains(new FastVector3Int(2, 2, 0)));
        }

        [Test]
        public void EnumerateAreaWithOffsetCenterReturnsCorrectPoints()
        {
            Circle circle = new(new Vector2(10f, 10f), 2f);
            HashSet<FastVector3Int> points = new(circle.EnumerateArea());

            foreach (FastVector3Int point in points)
            {
                Assert.IsTrue(
                    circle.Contains(new Vector2(point.x, point.y)),
                    $"Point ({point.x}, {point.y}) should be within circle at (10, 10) with radius 2"
                );
            }

            Assert.IsTrue(points.Contains(new FastVector3Int(10, 10, 0)));
            Assert.IsTrue(points.Contains(new FastVector3Int(11, 10, 0)));
            Assert.IsTrue(points.Contains(new FastVector3Int(10, 11, 0)));

            Assert.IsFalse(points.Contains(new FastVector3Int(0, 0, 0)));
            Assert.IsFalse(points.Contains(new FastVector3Int(15, 15, 0)));
        }

        [Test]
        public void EnumerateAreaHandlesNegativeCenterCoordinates()
        {
            Circle circle = new(new Vector2(-5f, -5f), 2f);
            HashSet<FastVector3Int> points = new(circle.EnumerateArea());

            foreach (FastVector3Int point in points)
            {
                Assert.IsTrue(circle.Contains(new Vector2(point.x, point.y)));
            }

            Assert.IsTrue(points.Contains(new FastVector3Int(-5, -5, 0)));
            Assert.IsTrue(points.Contains(new FastVector3Int(-4, -5, 0)));
        }

        [Test]
        public void EnumerateAreaWithCustomZCoordinate()
        {
            Circle circle = new(new Vector2(0f, 0f), 2f);
            int customZ = 5;
            HashSet<FastVector3Int> points = new(circle.EnumerateArea(customZ));

            Assert.IsTrue(0 < points.Count);
            foreach (FastVector3Int point in points)
            {
                Assert.AreEqual(customZ, point.z);
            }
        }

        [Test]
        public void EnumerateAreaWithZeroRadiusReturnsOnlyCenter()
        {
            Circle circle = new(new Vector2(5f, 5f), 0f);
            List<FastVector3Int> points = circle.EnumerateArea().ToList();

            Assert.AreEqual(1, points.Count);
            Assert.AreEqual(new FastVector3Int(5, 5, 0), points[0]);
        }

        [Test]
        public void EnumerateAreaWithSmallRadiusReturnsExpectedPoints()
        {
            Circle circle = new(new Vector2(0f, 0f), 1f);
            HashSet<FastVector3Int> points = new(circle.EnumerateArea());

            Assert.IsTrue(points.Contains(new FastVector3Int(0, 0, 0)));
            Assert.IsTrue(points.Contains(new FastVector3Int(1, 0, 0)));
            Assert.IsTrue(points.Contains(new FastVector3Int(-1, 0, 0)));
            Assert.IsTrue(points.Contains(new FastVector3Int(0, 1, 0)));
            Assert.IsTrue(points.Contains(new FastVector3Int(0, -1, 0)));

            Assert.IsFalse(points.Contains(new FastVector3Int(1, 1, 0)));
        }

        [Test]
        public void EnumerateAreaWithLargeRadiusReturnsCorrectCount()
        {
            Circle circle = new(new Vector2(0f, 0f), 10f);
            List<FastVector3Int> points = circle.EnumerateArea().ToList();

            int expectedCount = Mathf.RoundToInt(Mathf.PI * 10f * 10f);
            int tolerance = 20;

            Assert.IsTrue(
                Mathf.Abs(points.Count - expectedCount) <= tolerance,
                $"Expected approximately {expectedCount} points, got {points.Count}"
            );
        }

        [Test]
        public void EnumerateAreaBufferClearsBufferBeforeUse()
        {
            Circle circle = new(new Vector2(0f, 0f), 2f);
            List<FastVector3Int> buffer = new() { new FastVector3Int(999, 999, 999) };

            circle.EnumerateArea(buffer);

            Assert.IsFalse(buffer.Contains(new FastVector3Int(999, 999, 999)));
        }

        [Test]
        public void EnumerateAreaBufferReturnsCorrectPoints()
        {
            Circle circle = new(new Vector2(5f, 5f), 3f);
            List<FastVector3Int> buffer = new();

            List<FastVector3Int> result = circle.EnumerateArea(buffer);

            Assert.AreSame(buffer, result);

            foreach (FastVector3Int point in buffer)
            {
                Assert.IsTrue(circle.Contains(new Vector2(point.x, point.y)));
            }

            Assert.IsTrue(buffer.Contains(new FastVector3Int(5, 5, 0)));
        }

        [Test]
        public void EnumerateAreaBufferAndEnumerableReturnSamePoints()
        {
            Circle circle = new(new Vector2(7f, 3f), 4f);

            HashSet<FastVector3Int> enumerablePoints = new(circle.EnumerateArea());
            List<FastVector3Int> buffer = new();
            circle.EnumerateArea(buffer);
            HashSet<FastVector3Int> bufferPoints = new(buffer);

            Assert.AreEqual(enumerablePoints.Count, bufferPoints.Count);

            foreach (FastVector3Int point in enumerablePoints)
            {
                Assert.IsTrue(
                    bufferPoints.Contains(point),
                    $"Point {point} from enumerable not found in buffer"
                );
            }
        }

        [Test]
        public void EnumerateAreaHandlesFractionalCenterCorrectly()
        {
            Circle circle = new(new Vector2(2.5f, 2.5f), 2f);
            HashSet<FastVector3Int> points = new(circle.EnumerateArea());

            foreach (FastVector3Int point in points)
            {
                Assert.IsTrue(circle.Contains(new Vector2(point.x, point.y)));
            }

            Assert.IsTrue(points.Contains(new FastVector3Int(2, 2, 0)));

            Assert.IsTrue(points.Contains(new FastVector3Int(3, 3, 0)));
        }

        [Test]
        public void EnumerateAreaDoesNotIncludePointsOutsideRadius()
        {
            Circle circle = new(new Vector2(0f, 0f), 3f);
            HashSet<FastVector3Int> points = new(circle.EnumerateArea());

            Assert.IsFalse(points.Contains(new FastVector3Int(3, 3, 0)));
        }

        [Test]
        public void EnumerateAreaIncludesPointsOnCircumference()
        {
            Circle circle = new(new Vector2(0f, 0f), 5f);
            HashSet<FastVector3Int> points = new(circle.EnumerateArea());

            Assert.IsTrue(points.Contains(new FastVector3Int(5, 0, 0)));
            Assert.IsTrue(points.Contains(new FastVector3Int(0, 5, 0)));
            Assert.IsTrue(points.Contains(new FastVector3Int(-5, 0, 0)));
            Assert.IsTrue(points.Contains(new FastVector3Int(0, -5, 0)));

            Assert.IsTrue(points.Contains(new FastVector3Int(3, 4, 0)));
            Assert.IsTrue(points.Contains(new FastVector3Int(4, 3, 0)));
        }
    }
}
