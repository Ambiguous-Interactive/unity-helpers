// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class LineHelperTests : CommonTestBase
    {
        [Test]
        public void SimplifyWithNullPointsReturnsEmptyBuffer()
        {
            List<Vector2> result = LineHelper.Simplify(null, 1.0f);

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void SimplifyWithEmptyPointsReturnsEmptyBuffer()
        {
            List<Vector2> points = new();

            List<Vector2> result = LineHelper.Simplify(points, 1.0f);

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void SimplifyWithOnePointReturnsOriginalPoint()
        {
            List<Vector2> points = new() { Vector2.zero };

            List<Vector2> result = LineHelper.Simplify(points, 1.0f);

            Assert.That(result.Count, Is.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void SimplifyWithTwoPointsReturnsOriginalPoints()
        {
            List<Vector2> points = new() { Vector2.zero, Vector2.one };

            List<Vector2> result = LineHelper.Simplify(points, 1.0f);

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(Vector2.zero));
            Assert.That(result[1], Is.EqualTo(Vector2.one));
        }

        [Test]
        public void SimplifyWithZeroEpsilonReturnsAllPoints()
        {
            List<Vector2> points = new()
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(2f, 2f),
            };

            List<Vector2> result = LineHelper.Simplify(points, 0f);

            Assert.AreNotSame(points, result);
            Assert.That(result, Is.EqualTo(points));
        }

        [Test]
        public void SimplifyWithNegativeEpsilonReturnsAllPoints()
        {
            List<Vector2> points = new()
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(2f, 2f),
            };

            List<Vector2> result = LineHelper.Simplify(points, -1.0f);

            Assert.That(result, Is.EqualTo(points));
        }

        [Test]
        public void SimplifyPreservesSignificantDeviation()
        {
            List<Vector2> points = new()
            {
                new Vector2(0f, 0f),
                new Vector2(0.5f, 1f),
                new Vector2(1f, 0f),
            };

            List<Vector2> result = LineHelper.Simplify(points, 0.2f);

            Assert.That(result, Is.EqualTo(points));
        }

        [Test]
        public void SimplifyRemovesColinearPoints()
        {
            List<Vector2> points = new()
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(2f, 0f),
            };

            List<Vector2> result = LineHelper.Simplify(points, 0.01f);

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(points[0]));
            Assert.That(result[1], Is.EqualTo(points[2]));
        }

        [Test]
        public void SimplifyRemovesPointsWithinTolerance()
        {
            List<Vector2> points = new()
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0.01f),
                new Vector2(2f, 0f),
            };

            List<Vector2> result = LineHelper.Simplify(points, 0.1f);

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(points[0]));
            Assert.That(result[1], Is.EqualTo(points[2]));
        }

        [Test]
        public void SimplifyHandlesComplexPath()
        {
            List<Vector2> points = new()
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0.1f),
                new Vector2(2f, -0.1f),
                new Vector2(3f, 5f),
                new Vector2(4f, 6f),
                new Vector2(5f, 7f),
                new Vector2(6f, 8.1f),
                new Vector2(7f, 9f),
                new Vector2(8f, 9f),
            };

            List<Vector2> result = LineHelper.Simplify(points, 0.5f);

            Assert.That(result.Count, Is.LessThan(points.Count));
            Assert.That(result[0], Is.EqualTo(points[0]));
            Assert.That(result[^1], Is.EqualTo(points[^1]));
        }

        [Test]
        public void SimplifyUsesProvidedBuffer()
        {
            List<Vector2> points = new() { Vector2.zero, Vector2.one };
            List<Vector2> buffer = new();

            List<Vector2> result = LineHelper.Simplify(points, 1.0f, buffer);

            Assert.AreSame(buffer, result);
        }

        [Test]
        public void SimplifyClearsProvidedBuffer()
        {
            List<Vector2> points = new() { Vector2.zero, Vector2.one };
            List<Vector2> buffer = new() { Vector2.up, Vector2.down, Vector2.left };

            List<Vector2> result = LineHelper.Simplify(points, 1.0f, buffer);

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(Vector2.zero));
            Assert.That(result[1], Is.EqualTo(Vector2.one));
        }

        [Test]
        public void SimplifyHandlesVerticalLine()
        {
            List<Vector2> points = new()
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(0f, 2f),
            };

            List<Vector2> result = LineHelper.Simplify(points, 0.01f);

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(points[0]));
            Assert.That(result[1], Is.EqualTo(points[2]));
        }

        [Test]
        public void SimplifyHandlesDiagonalLine()
        {
            List<Vector2> points = new()
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(2f, 2f),
                new Vector2(3f, 3f),
            };

            List<Vector2> result = LineHelper.Simplify(points, 0.01f);

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(points[0]));
            Assert.That(result[1], Is.EqualTo(points[3]));
        }

        [Test]
        public void SimplifyHandlesLargeEpsilon()
        {
            List<Vector2> points = new()
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(2f, 0f),
            };

            List<Vector2> result = LineHelper.Simplify(points, 100f);

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(points[0]));
            Assert.That(result[1], Is.EqualTo(points[2]));
        }

        [Test]
        public void SimplifyPreciseWithNullPointsReturnsNull()
        {
            List<Vector2> result = LineHelper.SimplifyPrecise(null, 0.5);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void SimplifyPreciseWithEmptyPointsReturnsEmpty()
        {
            List<Vector2> points = new();

            List<Vector2> result = LineHelper.SimplifyPrecise(points, 0.5);

            Assert.That(result, Is.EqualTo(points));
        }

        [Test]
        public void SimplifyPreciseWithOnePointReturnsOriginal()
        {
            List<Vector2> points = new() { Vector2.zero };

            List<Vector2> result = LineHelper.SimplifyPrecise(points, 0.5);

            Assert.That(result, Is.EqualTo(points));
        }

        [Test]
        public void SimplifyPreciseWithTwoPointsReturnsOriginal()
        {
            List<Vector2> points = new() { Vector2.zero, Vector2.one };

            List<Vector2> result = LineHelper.SimplifyPrecise(points, 0.5);

            Assert.That(result, Is.EqualTo(points));
        }

        [Test]
        public void SimplifyPreciseUsesProvidedBufferForShortInputs()
        {
            List<Vector2>[] inputs =
            {
                null,
                new(),
                new() { Vector2.zero },
                new() { Vector2.zero, Vector2.one },
            };

            foreach (List<Vector2> points in inputs)
            {
                List<Vector2> buffer = new() { Vector2.up };
                List<Vector2> result = LineHelper.SimplifyPrecise(points, 0.5, buffer);

                Assert.AreSame(buffer, result);
                if (points == null)
                {
                    CollectionAssert.IsEmpty(result);
                }
                else
                {
                    CollectionAssert.AreEqual(points, result);
                }
            }
        }

        [Test]
        public void SimplifyPrecisePreservesShortInputWhenItIsTheBuffer()
        {
            List<Vector2> points = new() { Vector2.zero, Vector2.one };

            List<Vector2> result = LineHelper.SimplifyPrecise(points, 0.5, points);

            Assert.AreSame(points, result);
            CollectionAssert.AreEqual(new[] { Vector2.zero, Vector2.one }, result);
        }

        [Test]
        public void SimplifyPreciseCompactsAliasedBuffer()
        {
            List<Vector2> points = new()
            {
                Vector2.zero,
                Vector2.right,
                Vector2.right * 2f,
                Vector2.right * 3f,
            };

            List<Vector2> result = LineHelper.SimplifyPrecise(points, 0.1, points);

            Assert.AreSame(points, result);
            CollectionAssert.AreEqual(new[] { Vector2.zero, Vector2.right * 3f }, result);
        }

        [Test]
        public void SimplifyPrecisePreservesOnePointInAliasedDuplicatePath()
        {
            List<Vector2> points = new() { Vector2.one, Vector2.one, Vector2.one };

            List<Vector2> result = LineHelper.SimplifyPrecise(points, 0.1, points);

            Assert.AreSame(points, result);
            CollectionAssert.AreEqual(new[] { Vector2.one }, result);
        }

        [Test]
        public void SimplifySupportsAliasedBuffer()
        {
            List<Vector2> points = new() { Vector2.zero, Vector2.right, Vector2.right * 2f };

            List<Vector2> result = LineHelper.Simplify(points, 0.1f, points);

            Assert.AreSame(points, result);
            CollectionAssert.AreEqual(new[] { Vector2.zero, Vector2.right * 2f }, result);

            points.Insert(1, Vector2.right);
            result = LineHelper.Simplify(points, 0f, points);
            Assert.AreSame(points, result);
            CollectionAssert.AreEqual(
                new[] { Vector2.zero, Vector2.right, Vector2.right * 2f },
                result
            );
        }

        [Test]
        public void SimplifyPreciseRemovesRedundantColinearPoints()
        {
            List<Vector2> points = new()
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(2f, 0f),
            };

            List<Vector2> result = LineHelper.SimplifyPrecise(points, 0.01);

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(points[0]));
            Assert.That(result[1], Is.EqualTo(points[2]));
        }

        [Test]
        public void SimplifyPrecisePreservesSignificantDeviation()
        {
            List<Vector2> points = new()
            {
                new Vector2(0f, 0f),
                new Vector2(0.5f, 1f),
                new Vector2(1f, 0f),
            };

            List<Vector2> result = LineHelper.SimplifyPrecise(points, 0.1);

            Assert.That(result, Is.EqualTo(points));
        }

        [Test]
        public void SimplifyPreciseRemovesPointsWithinTolerance()
        {
            List<Vector2> points = new()
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0.01f),
                new Vector2(2f, 0f),
            };

            List<Vector2> result = LineHelper.SimplifyPrecise(points, 0.1);

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(points[0]));
            Assert.That(result[1], Is.EqualTo(points[2]));
        }

        [Test]
        public void SimplifyPreciseHandlesComplexPath()
        {
            List<Vector2> points = new()
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0.1f),
                new Vector2(2f, -0.1f),
                new Vector2(3f, 5f),
                new Vector2(4f, 6f),
                new Vector2(5f, 7f),
                new Vector2(6f, 8.1f),
                new Vector2(7f, 9f),
                new Vector2(8f, 9f),
            };

            List<Vector2> result = LineHelper.SimplifyPrecise(points, 0.5);

            Assert.That(result.Count, Is.LessThan(points.Count));
            Assert.That(result[0], Is.EqualTo(points[0]));
            Assert.That(result[^1], Is.EqualTo(points[^1]));
        }

        [Test]
        public void SimplifyPreciseHandlesVerticalLine()
        {
            List<Vector2> points = new()
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(0f, 2f),
            };

            List<Vector2> result = LineHelper.SimplifyPrecise(points, 0.01);

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(points[0]));
            Assert.That(result[1], Is.EqualTo(points[2]));
        }

        [Test]
        public void SimplifyPreciseHandlesDiagonalLine()
        {
            List<Vector2> points = new()
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(2f, 2f),
                new Vector2(3f, 3f),
            };

            List<Vector2> result = LineHelper.SimplifyPrecise(points, 0.01);

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(points[0]));
            Assert.That(result[1], Is.EqualTo(points[3]));
        }

        [Test]
        public void SimplifyPreciseHandlesZeroTolerance()
        {
            List<Vector2> points = new()
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(2f, 0f),
            };

            List<Vector2> result = LineHelper.SimplifyPrecise(points, 0.0);

            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void SimplifyPreciseHandlesNegativeTolerance()
        {
            List<Vector2> points = new()
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(2f, 0f),
            };

            List<Vector2> result = LineHelper.SimplifyPrecise(points, -1.0);

            Assert.That(result.Count, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void SimplifyPreciseHandlesLargeTolerance()
        {
            List<Vector2> points = new()
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(2f, 0f),
            };

            List<Vector2> result = LineHelper.SimplifyPrecise(points, 1000.0);

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(points[0]));
            Assert.That(result[1], Is.EqualTo(points[2]));
        }

        [Test]
        public void SimplifyPreciseHandlesDuplicateFirstAndLastPoints()
        {
            List<Vector2> points = new()
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(2f, 2f),
                new Vector2(0f, 0f),
            };

            List<Vector2> result = LineHelper.SimplifyPrecise(points, 0.01);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void SimplifyPreciseHandlesAllDuplicatePoints()
        {
            List<Vector2> points = new()
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
            };

            List<Vector2> result = LineHelper.SimplifyPrecise(points, 0.01);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Count, Is.GreaterThan(0));
        }

        [Test]
        public void SimplifyPreciseHandlesSquarePath()
        {
            List<Vector2> points = new()
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 0f),
            };

            List<Vector2> result = LineHelper.SimplifyPrecise(points, 0.01);

            Assert.That(result.Count, Is.GreaterThanOrEqualTo(4));
        }

        [Test]
        public void SimplifyPreciseHandlesCircularApproximation()
        {
            List<Vector2> points = new();
            int segments = 36;
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * 2f * Mathf.PI / segments;
                points.Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)));
            }

            List<Vector2> result = LineHelper.SimplifyPrecise(points, 0.1);

            Assert.That(result.Count, Is.LessThan(points.Count));
            Assert.That(result.Count, Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void SimplifyPreciseHandlesZigzagPattern()
        {
            List<Vector2> points = new()
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(2f, 0f),
                new Vector2(3f, 1f),
                new Vector2(4f, 0f),
            };

            List<Vector2> result = LineHelper.SimplifyPrecise(points, 0.1);

            Assert.That(result.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(result[0], Is.EqualTo(points[0]));
            Assert.That(result[^1], Is.EqualTo(points[^1]));
        }

        [Test]
        public void SimplifyPreciseReturnsNewListNotSameReference()
        {
            List<Vector2> points = new()
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(2f, 2f),
            };

            List<Vector2> result = LineHelper.SimplifyPrecise(points, 0.01);

            Assert.AreNotSame(points, result);
        }

        [Test]
        public void SimplifyPreciseHandlesLargeDataset()
        {
            List<Vector2> points = new();
            for (int i = 0; i < 1000; i++)
            {
                points.Add(new Vector2(i, Mathf.Sin(i * 0.1f)));
            }

            List<Vector2> result = LineHelper.SimplifyPrecise(points, 0.5);

            Assert.That(result.Count, Is.LessThan(points.Count));
            Assert.That(result[0], Is.EqualTo(points[0]));
            Assert.That(result[^1], Is.EqualTo(points[^1]));
        }

        [Test]
        public void SimplifyHandlesVerySmallEpsilon()
        {
            List<Vector2> points = new()
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0.00001f),
                new Vector2(2f, 0f),
            };

            List<Vector2> result = LineHelper.Simplify(points, 0.000001f);

            Assert.That(result, Is.EqualTo(points));
        }

        [Test]
        public void SimplifyHandlesVeryLargeCoordinates()
        {
            List<Vector2> points = new()
            {
                new Vector2(1000000f, 1000000f),
                new Vector2(1000001f, 1000000f),
                new Vector2(1000002f, 1000000f),
            };

            List<Vector2> result = LineHelper.Simplify(points, 0.01f);

            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void SimplifyPreciseHandlesVeryLargeCoordinates()
        {
            List<Vector2> points = new()
            {
                new Vector2(1000000f, 1000000f),
                new Vector2(1000001f, 1000000f),
                new Vector2(1000002f, 1000000f),
            };

            List<Vector2> result = LineHelper.SimplifyPrecise(points, 0.01);

            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void SimplifyHandlesNearZeroCoordinates()
        {
            List<Vector2> points = new()
            {
                new Vector2(0.0001f, 0.0001f),
                new Vector2(0.0002f, 0.0001f),
                new Vector2(0.0003f, 0.0001f),
            };

            List<Vector2> result = LineHelper.Simplify(points, 0.00001f);

            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void SimplifyHandlesNegativeCoordinates()
        {
            List<Vector2> points = new()
            {
                new Vector2(-10f, -10f),
                new Vector2(-5f, -5f),
                new Vector2(0f, 0f),
            };

            List<Vector2> result = LineHelper.Simplify(points, 0.01f);

            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void SimplifyPreciseHandlesNegativeCoordinates()
        {
            List<Vector2> points = new()
            {
                new Vector2(-10f, -10f),
                new Vector2(-5f, -5f),
                new Vector2(0f, 0f),
            };

            List<Vector2> result = LineHelper.SimplifyPrecise(points, 0.01);

            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void SimplifyHandlesMixedPositiveNegativeCoordinates()
        {
            List<Vector2> points = new()
            {
                new Vector2(-5f, 5f),
                new Vector2(0f, 0f),
                new Vector2(5f, -5f),
            };

            List<Vector2> result = LineHelper.Simplify(points, 0.01f);

            Assert.That(result.Count, Is.EqualTo(2));
        }

        [Test]
        public void SimplifyHandlesFloatingPointPrecisionIssues()
        {
            List<Vector2> points = new()
            {
                new Vector2(0f, 0f),
                new Vector2(0.1f + 0.2f, 0f),
                new Vector2(0.3f, 0f),
            };

            List<Vector2> result = LineHelper.Simplify(points, 0.0001f);

            Assert.That(result.Count, Is.GreaterThanOrEqualTo(2));
        }
    }
}
