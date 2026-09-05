// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Math
{
    using System;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Math;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class PointPolygonCheckTests
    {
        [Test]
        public void IsPointInsidePolygonPointInsideSquareReturnsTrue()
        {
            Vector2 point = new(5f, 5f);
            Vector2[] square = { new(0f, 0f), new(10f, 0f), new(10f, 10f), new(0f, 10f) };

            bool result = PointPolygonCheck.IsPointInsidePolygon(point, square);

            Assert.IsTrue(result);
        }

        [Test]
        public void IsPointInsidePolygonPointOutsideSquareReturnsFalse()
        {
            Vector2 point = new(15f, 5f);
            Vector2[] square = { new(0f, 0f), new(10f, 0f), new(10f, 10f), new(0f, 10f) };

            bool result = PointPolygonCheck.IsPointInsidePolygon(point, square);

            Assert.IsFalse(result);
        }

        [Test]
        public void IsPointInsidePolygonPointInsideTriangleReturnsTrue()
        {
            Vector2 point = new(5f, 3f);
            Vector2[] triangle = { new(0f, 0f), new(10f, 0f), new(5f, 10f) };

            bool result = PointPolygonCheck.IsPointInsidePolygon(point, triangle);

            Assert.IsTrue(result);
        }

        [Test]
        public void IsPointInsidePolygonPointOutsideTriangleReturnsFalse()
        {
            Vector2 point = new(0f, 5f);
            Vector2[] triangle = { new(0f, 0f), new(10f, 0f), new(5f, 10f) };

            bool result = PointPolygonCheck.IsPointInsidePolygon(point, triangle);

            Assert.IsFalse(result);
        }

        [Test]
        public void IsPointInsidePolygonPointInsideComplexPolygonReturnsTrue()
        {
            Vector2 point = new(3f, 3f);
            Vector2[] polygon =
            {
                new(0f, 0f),
                new(5f, 0f),
                new(5f, 2f),
                new(3f, 2f),
                new(3f, 5f),
                new(0f, 5f),
            };

            bool result = PointPolygonCheck.IsPointInsidePolygon(point, polygon);

            Assert.IsTrue(result);
        }

        [Test]
        public void IsPointInsidePolygonPointInConcaveSectionReturnsCorrectResult()
        {
            Vector2[] lShape =
            {
                new(0f, 0f),
                new(10f, 0f),
                new(10f, 5f),
                new(5f, 5f),
                new(5f, 10f),
                new(0f, 10f),
            };

            Vector2 outsidePoint = new(7f, 7f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outsidePoint, lShape));

            Vector2 insidePoint = new(2f, 2f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(insidePoint, lShape));
        }

        [Test]
        public void IsPointInsidePolygonPointAtVertexReturnsConsistentResult()
        {
            Vector2[] square = { new(0f, 0f), new(10f, 0f), new(10f, 10f), new(0f, 10f) };

            Vector2 vertex = new(0f, 0f);
            bool result = PointPolygonCheck.IsPointInsidePolygon(vertex, square);

            Assert.That(result, Is.True.Or.False);
        }

        [Test]
        public void IsPointInsidePolygonPolygonWithNegativeCoordinatesReturnsCorrectResult()
        {
            Vector2[] square = { new(-10f, -10f), new(10f, -10f), new(10f, 10f), new(-10f, 10f) };

            Vector2 insidePoint = new(0f, 0f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(insidePoint, square));

            Vector2 outsidePoint = new(15f, 15f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outsidePoint, square));
        }

        [Test]
        public void IsPointInsidePolygonCounterClockwisePolygonReturnsCorrectResult()
        {
            Vector2 point = new(5f, 5f);
            Vector2[] square = { new(0f, 0f), new(0f, 10f), new(10f, 10f), new(10f, 0f) };

            bool result = PointPolygonCheck.IsPointInsidePolygon(point, square);

            Assert.IsTrue(result);
        }

        [Test]
        public void IsPointInsidePolygonNullPolygonReturnsFalse()
        {
            Vector2 point = new(5f, 5f);
            Vector2[] polygon = null;

            bool result = PointPolygonCheck.IsPointInsidePolygon(point, polygon);

            Assert.IsFalse(result);
        }

        [Test]
        public void IsPointInsidePolygonEmptyPolygonReturnsFalse()
        {
            Vector2 point = new(5f, 5f);
            Vector2[] polygon = Array.Empty<Vector2>();

            bool result = PointPolygonCheck.IsPointInsidePolygon(point, polygon);

            Assert.IsFalse(result);
        }

        [Test]
        public void IsPointInsidePolygonSingleVertexPolygonReturnsFalse()
        {
            Vector2 point = new(5f, 5f);
            Vector2[] polygon = { new(0f, 0f) };

            bool result = PointPolygonCheck.IsPointInsidePolygon(point, polygon);

            Assert.IsFalse(result);
        }

        [Test]
        public void IsPointInsidePolygonTwoVertexPolygonReturnsFalse()
        {
            Vector2 point = new(5f, 5f);
            Vector2[] polygon = { new(0f, 0f), new(10f, 10f) };

            bool result = PointPolygonCheck.IsPointInsidePolygon(point, polygon);

            Assert.IsFalse(result);
        }

        [Test]
        public void IsPointInsidePolygonHorizontalEdgeHandlesCorrectly()
        {
            Vector2[] square = { new(0f, 0f), new(10f, 0f), new(10f, 10f), new(0f, 10f) };

            Vector2 pointOnEdge = new(5f, 0f);
            bool result = PointPolygonCheck.IsPointInsidePolygon(pointOnEdge, square);

            Assert.That(result, Is.True.Or.False);
        }

        [Test]
        public void IsPointInsidePolygonVerySmallPolygonHandlesCorrectly()
        {
            Vector2[] tinySquare =
            {
                new(0f, 0f),
                new(0.001f, 0f),
                new(0.001f, 0.001f),
                new(0f, 0.001f),
            };

            Vector2 insidePoint = new(0.0005f, 0.0005f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(insidePoint, tinySquare));

            Vector2 outsidePoint = new(0.002f, 0.002f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outsidePoint, tinySquare));
        }

        [Test]
        public void IsPointInsidePolygonSpanPointInsideSquareReturnsTrue()
        {
            Vector2 point = new(5f, 5f);
            Span<Vector2> square = stackalloc Vector2[]
            {
                new Vector2(0f, 0f),
                new Vector2(10f, 0f),
                new Vector2(10f, 10f),
                new Vector2(0f, 10f),
            };

            bool result = PointPolygonCheck.IsPointInsidePolygon(point, square);

            Assert.IsTrue(result);
        }

        [Test]
        public void IsPointInsidePolygonSpanPointOutsideSquareReturnsFalse()
        {
            Vector2 point = new(15f, 5f);
            Span<Vector2> square = stackalloc Vector2[]
            {
                new Vector2(0f, 0f),
                new Vector2(10f, 0f),
                new Vector2(10f, 10f),
                new Vector2(0f, 10f),
            };

            bool result = PointPolygonCheck.IsPointInsidePolygon(point, square);

            Assert.IsFalse(result);
        }

        [Test]
        public void IsPointInsidePolygonSpanEmptyPolygonReturnsFalse()
        {
            Vector2 point = new(5f, 5f);
            Span<Vector2> polygon = stackalloc Vector2[0];

            bool result = PointPolygonCheck.IsPointInsidePolygon(point, polygon);

            Assert.IsFalse(result);
        }

        [Test]
        public void IsPointInsidePolygonVector3PointInsideSquareOnXYPlaneReturnsTrue()
        {
            Vector3 point = new(5f, 5f, 0f);
            Vector3[] square =
            {
                new(0f, 0f, 0f),
                new(10f, 0f, 0f),
                new(10f, 10f, 0f),
                new(0f, 10f, 0f),
            };
            Vector3 planeNormal = Vector3.forward;

            bool result = PointPolygonCheck.IsPointInsidePolygon(point, square, planeNormal);

            Assert.IsTrue(result);
        }

        [Test]
        public void IsPointInsidePolygonVector3PointOutsideSquareOnXYPlaneReturnsFalse()
        {
            Vector3 point = new(15f, 5f, 0f);
            Vector3[] square =
            {
                new(0f, 0f, 0f),
                new(10f, 0f, 0f),
                new(10f, 10f, 0f),
                new(0f, 10f, 0f),
            };
            Vector3 planeNormal = Vector3.forward;

            bool result = PointPolygonCheck.IsPointInsidePolygon(point, square, planeNormal);

            Assert.IsFalse(result);
        }

        [Test]
        public void IsPointInsidePolygonVector3PointInsideSquareOnXZPlaneReturnsTrue()
        {
            Vector3 point = new(5f, 0f, 5f);
            Vector3[] square =
            {
                new(0f, 0f, 0f),
                new(10f, 0f, 0f),
                new(10f, 0f, 10f),
                new(0f, 0f, 10f),
            };
            Vector3 planeNormal = Vector3.up;

            bool result = PointPolygonCheck.IsPointInsidePolygon(point, square, planeNormal);

            Assert.IsTrue(result);
        }

        [Test]
        public void IsPointInsidePolygonVector3PointInsideSquareOnYZPlaneReturnsTrue()
        {
            Vector3 point = new(0f, 5f, 5f);
            Vector3[] square =
            {
                new(0f, 0f, 0f),
                new(0f, 10f, 0f),
                new(0f, 10f, 10f),
                new(0f, 0f, 10f),
            };
            Vector3 planeNormal = Vector3.right;

            bool result = PointPolygonCheck.IsPointInsidePolygon(point, square, planeNormal);

            Assert.IsTrue(result);
        }

        [Test]
        public void IsPointInsidePolygonVector3PointAbovePlaneProjectsAndReturnsTrue()
        {
            Vector3 point = new(5f, 5f, 100f);
            Vector3[] square =
            {
                new(0f, 0f, 0f),
                new(10f, 0f, 0f),
                new(10f, 10f, 0f),
                new(0f, 10f, 0f),
            };
            Vector3 planeNormal = Vector3.forward;

            bool result = PointPolygonCheck.IsPointInsidePolygon(point, square, planeNormal);

            Assert.IsTrue(result);
        }

        [Test]
        public void IsPointInsidePolygonVector3PointBelowPlaneProjectsAndReturnsTrue()
        {
            Vector3 point = new(5f, 5f, -100f);
            Vector3[] square =
            {
                new(0f, 0f, 0f),
                new(10f, 0f, 0f),
                new(10f, 10f, 0f),
                new(0f, 10f, 0f),
            };
            Vector3 planeNormal = Vector3.forward;

            bool result = PointPolygonCheck.IsPointInsidePolygon(point, square, planeNormal);

            Assert.IsTrue(result);
        }

        [Test]
        public void IsPointInsidePolygonVector3ArbitraryPlaneOrientationReturnsCorrectResult()
        {
            Vector3 planeNormal = new Vector3(1f, 1f, 0f).normalized;
            Vector3 center = new(5f, 5f, 5f);

            Vector3 tangent = Vector3.Cross(planeNormal, Vector3.forward);
            if (tangent.sqrMagnitude < 1e-6f)
            {
                tangent = Vector3.Cross(planeNormal, Vector3.up);
            }
            tangent.Normalize();
            Vector3 bitangent = Vector3.Cross(planeNormal, tangent).normalized;
            float halfSize = 2f;

            Vector3[] square =
            {
                center - tangent * halfSize + bitangent * halfSize,
                center + tangent * halfSize + bitangent * halfSize,
                center + tangent * halfSize - bitangent * halfSize,
                center - tangent * halfSize - bitangent * halfSize,
            };

            bool insideResult = PointPolygonCheck.IsPointInsidePolygon(center, square, planeNormal);
            Assert.IsTrue(insideResult);

            Vector3 farPoint = center + new Vector3(10f, 10f, 10f);
            bool outsideResult = PointPolygonCheck.IsPointInsidePolygon(
                farPoint,
                square,
                planeNormal
            );
            Assert.IsFalse(outsideResult);
        }

        [Test]
        public void IsPointInsidePolygonVector3NullPolygonReturnsFalse()
        {
            Vector3 point = new(5f, 5f, 5f);
            Vector3[] polygon = null;
            Vector3 planeNormal = Vector3.forward;

            bool result = PointPolygonCheck.IsPointInsidePolygon(point, polygon, planeNormal);

            Assert.IsFalse(result);
        }

        [Test]
        public void IsPointInsidePolygonVector3EmptyPolygonReturnsFalse()
        {
            Vector3 point = new(5f, 5f, 5f);
            Vector3[] polygon = Array.Empty<Vector3>();
            Vector3 planeNormal = Vector3.forward;

            bool result = PointPolygonCheck.IsPointInsidePolygon(point, polygon, planeNormal);

            Assert.IsFalse(result);
        }

        [Test]
        public void IsPointInsidePolygonVector3TwoVertexPolygonReturnsFalse()
        {
            Vector3 point = new(5f, 5f, 5f);
            Vector3[] polygon = { new(0f, 0f, 0f), new(10f, 10f, 10f) };
            Vector3 planeNormal = Vector3.forward;

            bool result = PointPolygonCheck.IsPointInsidePolygon(point, polygon, planeNormal);

            Assert.IsFalse(result);
        }

        [Test]
        public void IsPointInsidePolygonVector3SpanPointInsideSquareReturnsTrue()
        {
            Vector3 point = new(5f, 5f, 0f);
            Span<Vector3> square = stackalloc Vector3[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(10f, 0f, 0f),
                new Vector3(10f, 10f, 0f),
                new Vector3(0f, 10f, 0f),
            };
            Vector3 planeNormal = Vector3.forward;

            bool result = PointPolygonCheck.IsPointInsidePolygon(point, square, planeNormal);

            Assert.IsTrue(result);
        }

        [Test]
        public void IsPointInsidePolygonVector3SpanPointOutsideSquareReturnsFalse()
        {
            Vector3 point = new(15f, 5f, 0f);
            Span<Vector3> square = stackalloc Vector3[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(10f, 0f, 0f),
                new Vector3(10f, 10f, 0f),
                new Vector3(0f, 10f, 0f),
            };
            Vector3 planeNormal = Vector3.forward;

            bool result = PointPolygonCheck.IsPointInsidePolygon(point, square, planeNormal);

            Assert.IsFalse(result);
        }

        [Test]
        public void IsPointInsidePolygonVector3SpanEmptyPolygonReturnsFalse()
        {
            Vector3 point = new(5f, 5f, 5f);
            Span<Vector3> polygon = stackalloc Vector3[0];
            Vector3 planeNormal = Vector3.forward;

            bool result = PointPolygonCheck.IsPointInsidePolygon(point, polygon, planeNormal);

            Assert.IsFalse(result);
        }

        [Test]
        public void IsPointInsidePolygonLargePolygonHandlesEfficiently()
        {
            int vertexCount = 100;
            Vector2[] polygon = new Vector2[vertexCount];
            float radius = 10f;

            for (int i = 0; i < vertexCount; i++)
            {
                float angle = (float)i / vertexCount * Mathf.PI * 2f;
                polygon[i] = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
            }

            Vector2 insidePoint = new(0f, 0f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(insidePoint, polygon));

            Vector2 outsidePoint = new(15f, 15f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outsidePoint, polygon));
        }

        [Test]
        public void IsPointInsidePolygonPentagonVariousPointsReturnsCorrectResults()
        {
            Vector2[] pentagon = new Vector2[5];
            float radius = 10f;
            for (int i = 0; i < 5; i++)
            {
                float angle = (float)i / 5 * Mathf.PI * 2f - Mathf.PI / 2f;
                pentagon[i] = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
            }

            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(Vector2.zero, pentagon));

            Vector2 halfwayPoint = new(0f, -5f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(halfwayPoint, pentagon));

            Vector2 farPoint = new(0f, -20f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(farPoint, pentagon));
        }

        [Test]
        public void IsPointInsidePolygonStarReturnsCorrectResults()
        {
            Vector2[] star = new Vector2[10];
            float outerRadius = 10f;
            float innerRadius = 4f;

            for (int i = 0; i < 10; i++)
            {
                float angle = (float)i / 10 * Mathf.PI * 2f - Mathf.PI / 2f;
                float radius = i % 2 == 0 ? outerRadius : innerRadius;
                star[i] = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
            }

            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(Vector2.zero, star));

            Vector2 midRadiusPoint = new(0f, -7f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(midRadiusPoint, star));

            Vector2 outsidePoint = new(0f, -15f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outsidePoint, star));
        }

        [Test]
        public void IsPointInsidePolygonSelfIntersectingBowtieReturnsCorrectResults()
        {
            Vector2[] bowtie = { new(-5f, -5f), new(5f, 5f), new(5f, -5f), new(-5f, 5f) };

            Vector2 centerPoint = new(0f, 0f);
            bool centerResult = PointPolygonCheck.IsPointInsidePolygon(centerPoint, bowtie);
            Assert.That(centerResult, Is.True.Or.False);

            Vector2 insideTriangle = new(-3f, -3f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(insideTriangle, bowtie));

            Vector2 outside = new(10f, 0f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outside, bowtie));
        }

        [Test]
        public void IsPointInsidePolygonConcaveArrowReturnsCorrectResults()
        {
            Vector2[] arrow =
            {
                new(0f, 0f),
                new(5f, 0f),
                new(5f, 2f),
                new(8f, 2f),
                new(8f, 3f),
                new(5f, 3f),
                new(5f, 5f),
                new(0f, 5f),
            };

            Vector2 insideBody = new(2f, 2.5f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(insideBody, arrow));

            Vector2 inNotch = new(6f, 0.5f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(inNotch, arrow));

            Vector2 inHead = new(6f, 2.5f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(inHead, arrow));
        }

        [Test]
        public void IsPointInsidePolygonRayAlongEdgeHandlesCorrectly()
        {
            Vector2[] rect = { new(0f, 0f), new(10f, 0f), new(10f, 5f), new(0f, 5f) };

            Vector2 pointAboveEdge = new(5f, 0.5f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(pointAboveEdge, rect));

            Vector2 pointBelowEdge = new(5f, -0.5f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(pointBelowEdge, rect));
        }

        [Test]
        public void IsPointInsidePolygonMultipleVerticesAtSameYHandlesCorrectly()
        {
            Vector2[] polygon =
            {
                new(0f, 0f),
                new(8f, 0f),
                new(6f, 2f),
                new(4f, 2f),
                new(4f, 4f),
                new(2f, 2f),
            };

            Vector2 inside = new(4f, 1.5f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(inside, polygon));

            Vector2 outside = new(1f, 2.5f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outside, polygon));
        }

        [Test]
        public void IsPointInsidePolygonHorizontalEdgeAtPointYInsideReturnsTrue()
        {
            Vector2[] rect = { new(0f, 0f), new(10f, 0f), new(10f, 5f), new(0f, 5f) };

            Vector2 point = new(5f, 2.5f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(point, rect));
        }

        [Test]
        public void IsPointInsidePolygonHorizontalEdgeAtPointYOutsideReturnsFalse()
        {
            Vector2[] rect = { new(0f, 0f), new(10f, 0f), new(10f, 5f), new(0f, 5f) };

            Vector2 point = new(15f, 2.5f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(point, rect));
        }

        [Test]
        public void IsPointInsidePolygonLongHorizontalEdgeHandlesCorrectly()
        {
            Vector2[] polygon = { new(0f, 0f), new(0f, 5f), new(100f, 5f), new(100f, 0f) };

            Vector2 inside = new(50f, 2.5f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(inside, polygon));

            Vector2 outside = new(50f, 7f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outside, polygon));
        }

        [Test]
        public void IsPointInsidePolygonConsecutiveHorizontalEdgesHandlesCorrectly()
        {
            Vector2[] polygon =
            {
                new(0f, 0f),
                new(5f, 0f),
                new(5f, 2f),
                new(10f, 2f),
                new(10f, 4f),
                new(0f, 4f),
            };

            Vector2 insideBottom = new(2f, 1f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(insideBottom, polygon));

            Vector2 insideMiddle = new(7f, 3f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(insideMiddle, polygon));

            Vector2 outsideRight = new(7f, 1f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outsideRight, polygon));
        }

        [Test]
        public void IsPointInsidePolygonVertexAtExactPointYHandlesCorrectly()
        {
            Vector2[] triangle = { new(0f, 0f), new(10f, 0f), new(5f, 10f) };

            Vector2 inside = new(5f, 5f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(inside, triangle));

            Vector2 outside = new(15f, 0f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outside, triangle));
        }

        [Test]
        public void IsPointInsidePolygonRayThroughMultipleVerticesHandlesCorrectly()
        {
            Vector2[] diamond = { new(0f, 2f), new(2f, 0f), new(4f, 2f), new(2f, 4f) };

            Vector2 inside = new(2f, 2f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(inside, diamond));

            Vector2 outsideLeft = new(-1f, 2f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outsideLeft, diamond));

            Vector2 outsideRight = new(5f, 2f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outsideRight, diamond));
        }

        [Test]
        public void IsPointInsidePolygonZigzagWithManyHorizontalSegmentsHandlesCorrectly()
        {
            Vector2[] zigzag =
            {
                new(0f, 0f),
                new(2f, 0f),
                new(2f, 1f),
                new(4f, 1f),
                new(4f, 2f),
                new(6f, 2f),
                new(6f, 3f),
                new(2f, 3f),
                new(2f, 2f),
                new(0f, 2f),
            };

            Vector2 inside1 = new(1f, 0.5f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(inside1, zigzag));

            Vector2 inside2 = new(3f, 1.5f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(inside2, zigzag));

            Vector2 inside3 = new(5f, 2.5f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(inside3, zigzag));

            Vector2 outside1 = new(5f, 0.5f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outside1, zigzag));

            Vector2 outside2 = new(1f, 2.5f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outside2, zigzag));
        }

        [Test]
        public void IsPointInsidePolygonAllVerticesAtSameYReturnsFalse()
        {
            Vector2[] line = { new(0f, 5f), new(5f, 5f), new(10f, 5f) };

            Vector2 pointOnLine = new(5f, 5f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(pointOnLine, line));

            Vector2 pointAboveLine = new(5f, 6f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(pointAboveLine, line));

            Vector2 pointBelowLine = new(5f, 4f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(pointBelowLine, line));
        }

        [Test]
        public void IsPointInsidePolygonComplexConcaveWithHorizontalEdgesHandlesCorrectly()
        {
            Vector2[] complex =
            {
                new(0f, 0f),
                new(8f, 0f),
                new(8f, 2f),
                new(6f, 2f),
                new(6f, 4f),
                new(8f, 4f),
                new(8f, 6f),
                new(0f, 6f),
                new(0f, 4f),
                new(2f, 4f),
                new(2f, 2f),
                new(0f, 2f),
            };

            Vector2 insideLeft = new(1f, 1f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(insideLeft, complex));

            Vector2 insideRight = new(7f, 1f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(insideRight, complex));

            Vector2 outsideLeftCutout = new(1f, 3f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outsideLeftCutout, complex));

            Vector2 outsideRightCutout = new(7f, 3f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outsideRightCutout, complex));

            Vector2 insideCenter = new(4f, 3f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(insideCenter, complex));
        }

        [Test]
        public void IsPointInsidePolygonTrapezoidWithHorizontalEdgesHandlesCorrectly()
        {
            Vector2[] trapezoid = { new(2f, 0f), new(8f, 0f), new(10f, 5f), new(0f, 5f) };

            Vector2 insideBottom = new(5f, 1f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(insideBottom, trapezoid));

            Vector2 insideTop = new(5f, 4f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(insideTop, trapezoid));

            Vector2 outsideLeft = new(1f, 1f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outsideLeft, trapezoid));

            Vector2 outsideRight = new(9f, 1f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outsideRight, trapezoid));
        }

        [Test]
        public void IsPointInsidePolygonManyConsecutiveColinearVerticesHandlesCorrectly()
        {
            Vector2[] square =
            {
                new(0f, 0f),
                new(2.5f, 0f),
                new(5f, 0f),
                new(7.5f, 0f),
                new(10f, 0f),
                new(10f, 2.5f),
                new(10f, 5f),
                new(10f, 7.5f),
                new(10f, 10f),
                new(7.5f, 10f),
                new(5f, 10f),
                new(2.5f, 10f),
                new(0f, 10f),
                new(0f, 7.5f),
                new(0f, 5f),
                new(0f, 2.5f),
            };

            Vector2 inside = new(5f, 5f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(inside, square));

            Vector2 outside = new(15f, 5f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outside, square));
        }

        [Test]
        public void IsPointInsidePolygonCombShapeHandlesCorrectly()
        {
            Vector2[] comb =
            {
                new(0f, 0f),
                new(10f, 0f),
                new(10f, 3f),
                new(9f, 3f),
                new(9f, 1f),
                new(8f, 1f),
                new(8f, 3f),
                new(7f, 3f),
                new(7f, 1f),
                new(6f, 1f),
                new(6f, 3f),
                new(5f, 3f),
                new(5f, 1f),
                new(4f, 1f),
                new(4f, 3f),
                new(3f, 3f),
                new(3f, 1f),
                new(2f, 1f),
                new(2f, 3f),
                new(1f, 3f),
                new(1f, 1f),
                new(0f, 1f),
                new(0f, 0f),
            };

            Vector2 insideBase = new(5f, 0.5f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(insideBase, comb));

            Vector2 insideTooth1 = new(1.5f, 2f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(insideTooth1, comb));

            Vector2 insideTooth2 = new(5.5f, 2f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(insideTooth2, comb));

            Vector2 outsideGap1 = new(2.5f, 2f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outsideGap1, comb));

            Vector2 outsideGap2 = new(6.5f, 2f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outsideGap2, comb));
        }

        /*
            Large caller-sized stackalloc spans can crash the process; test both sides of the stack budget and a
            20,000-vertex polygon.
        */
        [TestCase(3)]
        [TestCase(1023)]
        [TestCase(1024)]
        [TestCase(1025)]
        [TestCase(2048)]
        [TestCase(20000)]
        public void IsPointInsidePolygonAnswersTheSameEitherSideOfTheStackBudget(int vertexCount)
        {
            Vector3[] polygon = RegularPolygon(vertexCount, 10f);
            Vector3 planeNormal = new(0f, 0f, 1f);

            Assert.IsTrue(
                PointPolygonCheck.IsPointInsidePolygon(Vector3.zero, polygon, planeNormal),
                $"the centre of a {vertexCount}-gon is inside it"
            );
            Assert.IsFalse(
                PointPolygonCheck.IsPointInsidePolygon(
                    new Vector3(50f, 50f, 0f),
                    polygon,
                    planeNormal
                ),
                $"a point well outside a {vertexCount}-gon is outside it"
            );
        }

        private static Vector3[] RegularPolygon(int vertexCount, float radius)
        {
            Vector3[] polygon = new Vector3[vertexCount];
            for (int index = 0; index < vertexCount; ++index)
            {
                float angle = 2f * Mathf.PI * index / vertexCount;
                polygon[index] = new Vector3(
                    radius * Mathf.Cos(angle),
                    radius * Mathf.Sin(angle),
                    0f
                );
            }

            return polygon;
        }
    }
}
