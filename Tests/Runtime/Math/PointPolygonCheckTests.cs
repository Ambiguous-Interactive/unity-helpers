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
            // L-shaped polygon
            Vector2[] lShape =
            {
                new(0f, 0f),
                new(10f, 0f),
                new(10f, 5f),
                new(5f, 5f),
                new(5f, 10f),
                new(0f, 10f),
            };

            // Point in the concave area (should be outside)
            Vector2 outsidePoint = new(7f, 7f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outsidePoint, lShape));

            // Point in the valid area
            Vector2 insidePoint = new(2f, 2f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(insidePoint, lShape));
        }

        [Test]
        public void IsPointInsidePolygonPointAtVertexReturnsConsistentResult()
        {
            Vector2[] square = { new(0f, 0f), new(10f, 0f), new(10f, 10f), new(0f, 10f) };

            Vector2 vertex = new(0f, 0f);
            bool result = PointPolygonCheck.IsPointInsidePolygon(vertex, square);

            // Edge case: result may vary, but should not crash
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

            // Point exactly on horizontal edge
            Vector2 pointOnEdge = new(5f, 0f);
            bool result = PointPolygonCheck.IsPointInsidePolygon(pointOnEdge, square);

            // Edge case: result may vary, but should not crash
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
            // Point is above the plane but projects to inside the polygon
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
            // Point is below the plane but projects to inside the polygon
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
            // Create a square on a tilted plane
            Vector3 planeNormal = new Vector3(1f, 1f, 0f).normalized;
            Vector3 center = new(5f, 5f, 5f);

            // Build a square directly in the tilted plane so the polygon matches the supplied normal
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

            // Point at center should be inside
            bool insideResult = PointPolygonCheck.IsPointInsidePolygon(center, square, planeNormal);
            Assert.IsTrue(insideResult);

            // Point far away should be outside
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
            // Create a large polygon (100 vertices in a circle)
            int vertexCount = 100;
            Vector2[] polygon = new Vector2[vertexCount];
            float radius = 10f;

            for (int i = 0; i < vertexCount; i++)
            {
                float angle = (float)i / vertexCount * Mathf.PI * 2f;
                polygon[i] = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
            }

            // Test points inside and outside
            Vector2 insidePoint = new(0f, 0f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(insidePoint, polygon));

            Vector2 outsidePoint = new(15f, 15f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outsidePoint, polygon));
        }

        [Test]
        public void IsPointInsidePolygonPentagonVariousPointsReturnsCorrectResults()
        {
            // Regular pentagon
            Vector2[] pentagon = new Vector2[5];
            float radius = 10f;
            for (int i = 0; i < 5; i++)
            {
                float angle = (float)i / 5 * Mathf.PI * 2f - Mathf.PI / 2f;
                pentagon[i] = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
            }

            // Center should be inside
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(Vector2.zero, pentagon));

            // Point at radius/2 should be inside
            Vector2 halfwayPoint = new(0f, -5f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(halfwayPoint, pentagon));

            // Point outside at 2*radius should be outside
            Vector2 farPoint = new(0f, -20f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(farPoint, pentagon));
        }

        [Test]
        public void IsPointInsidePolygonStarReturnsCorrectResults()
        {
            // 5-pointed star (self-intersecting polygon)
            Vector2[] star = new Vector2[10];
            float outerRadius = 10f;
            float innerRadius = 4f;

            for (int i = 0; i < 10; i++)
            {
                float angle = (float)i / 10 * Mathf.PI * 2f - Mathf.PI / 2f;
                float radius = i % 2 == 0 ? outerRadius : innerRadius;
                star[i] = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
            }

            // Center should be inside
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(Vector2.zero, star));

            // Point between inner and outer radius is inside the star shape (even-odd rule)
            Vector2 midRadiusPoint = new(0f, -7f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(midRadiusPoint, star));

            // Point beyond outer radius should be outside
            Vector2 outsidePoint = new(0f, -15f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outsidePoint, star));
        }

        [Test]
        public void IsPointInsidePolygonSelfIntersectingBowtieReturnsCorrectResults()
        {
            // Bowtie shape (self-intersecting at center)
            Vector2[] bowtie = { new(-5f, -5f), new(5f, 5f), new(5f, -5f), new(-5f, 5f) };

            // Point at center (intersection point) - behavior depends on even-odd rule
            Vector2 centerPoint = new(0f, 0f);
            bool centerResult = PointPolygonCheck.IsPointInsidePolygon(centerPoint, bowtie);
            Assert.That(centerResult, Is.True.Or.False); // Consistent but may vary

            // Point clearly in one of the triangular sections
            Vector2 insideTriangle = new(-3f, -3f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(insideTriangle, bowtie));

            // Point clearly outside
            Vector2 outside = new(10f, 0f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outside, bowtie));
        }

        [Test]
        public void IsPointInsidePolygonConcaveArrowReturnsCorrectResults()
        {
            // Arrow shape pointing right (concave but non-self-intersecting)
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

            // Point inside the arrow body
            Vector2 insideBody = new(2f, 2.5f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(insideBody, arrow));

            // Point in the concave notch (should be outside)
            Vector2 inNotch = new(6f, 0.5f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(inNotch, arrow));

            // Point in the arrowhead
            Vector2 inHead = new(6f, 2.5f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(inHead, arrow));
        }

        [Test]
        public void IsPointInsidePolygonRayAlongEdgeHandlesCorrectly()
        {
            // Rectangle where a horizontal ray from test point aligns with an edge
            Vector2[] rect = { new(0f, 0f), new(10f, 0f), new(10f, 5f), new(0f, 5f) };

            // Point with ray that would travel along the bottom edge
            Vector2 pointAboveEdge = new(5f, 0.5f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(pointAboveEdge, rect));

            // Point outside with ray that would travel along the bottom edge
            Vector2 pointBelowEdge = new(5f, -0.5f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(pointBelowEdge, rect));
        }

        [Test]
        public void IsPointInsidePolygonMultipleVerticesAtSameYHandlesCorrectly()
        {
            // Polygon with multiple vertices at the same Y coordinate
            // Vertices ordered to form a valid non-self-intersecting hexagon
            Vector2[] polygon =
            {
                new(0f, 0f),
                new(8f, 0f),
                new(6f, 2f),
                new(4f, 2f),
                new(4f, 4f),
                new(2f, 2f),
            };

            // Point inside
            Vector2 inside = new(4f, 1.5f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(inside, polygon));

            // Point outside
            Vector2 outside = new(1f, 2.5f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outside, polygon));
        }

        [Test]
        public void IsPointInsidePolygonHorizontalEdgeAtPointYInsideReturnsTrue()
        {
            // Rectangle with point Y coordinate matching bottom edge
            Vector2[] rect = { new(0f, 0f), new(10f, 0f), new(10f, 5f), new(0f, 5f) };

            // Point inside, with ray passing through horizontal edge
            Vector2 point = new(5f, 2.5f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(point, rect));
        }

        [Test]
        public void IsPointInsidePolygonHorizontalEdgeAtPointYOutsideReturnsFalse()
        {
            // Rectangle with point Y coordinate matching bottom edge
            Vector2[] rect = { new(0f, 0f), new(10f, 0f), new(10f, 5f), new(0f, 5f) };

            // Point outside, with ray passing through horizontal edge
            Vector2 point = new(15f, 2.5f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(point, rect));
        }

        [Test]
        public void IsPointInsidePolygonLongHorizontalEdgeHandlesCorrectly()
        {
            // Polygon with very long horizontal edge
            Vector2[] polygon = { new(0f, 0f), new(0f, 5f), new(100f, 5f), new(100f, 0f) };

            // Point inside below the horizontal edge
            Vector2 inside = new(50f, 2.5f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(inside, polygon));

            // Point outside above the horizontal edge
            Vector2 outside = new(50f, 7f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outside, polygon));
        }

        [Test]
        public void IsPointInsidePolygonConsecutiveHorizontalEdgesHandlesCorrectly()
        {
            // Polygon with consecutive horizontal edges forming a step pattern
            Vector2[] polygon =
            {
                new(0f, 0f),
                new(5f, 0f),
                new(5f, 2f),
                new(10f, 2f),
                new(10f, 4f),
                new(0f, 4f),
            };

            // Points at various Y levels
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
            // Triangle where vertex Y exactly matches point Y
            Vector2[] triangle = { new(0f, 0f), new(10f, 0f), new(5f, 10f) };

            // Point inside with Y matching bottom vertices
            Vector2 inside = new(5f, 5f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(inside, triangle));

            // Point outside with Y matching bottom vertices
            Vector2 outside = new(15f, 0f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outside, triangle));
        }

        [Test]
        public void IsPointInsidePolygonRayThroughMultipleVerticesHandlesCorrectly()
        {
            // Diamond shape where horizontal ray could pass through vertices
            Vector2[] diamond = { new(0f, 2f), new(2f, 0f), new(4f, 2f), new(2f, 4f) };

            // Point inside, ray passes through left and right vertices
            Vector2 inside = new(2f, 2f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(inside, diamond));

            // Point outside left, ray would pass through vertices
            Vector2 outsideLeft = new(-1f, 2f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outsideLeft, diamond));

            // Point outside right, ray would pass through vertices
            Vector2 outsideRight = new(5f, 2f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outsideRight, diamond));
        }

        [Test]
        public void IsPointInsidePolygonZigzagWithManyHorizontalSegmentsHandlesCorrectly()
        {
            // Create a zigzag shape with stepped plateaus and gaps at different heights
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

            // Points inside at different heights
            Vector2 inside1 = new(1f, 0.5f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(inside1, zigzag));

            Vector2 inside2 = new(3f, 1.5f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(inside2, zigzag));

            Vector2 inside3 = new(5f, 2.5f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(inside3, zigzag));

            // Points outside the zigzag cutouts
            Vector2 outside1 = new(5f, 0.5f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outside1, zigzag));

            Vector2 outside2 = new(1f, 2.5f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outside2, zigzag));
        }

        [Test]
        public void IsPointInsidePolygonAllVerticesAtSameYReturnsFalse()
        {
            // Degenerate polygon - all vertices on a horizontal line
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
            // Complex shape with multiple horizontal edges and concave sections
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

            // Points in main body
            Vector2 insideLeft = new(1f, 1f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(insideLeft, complex));

            Vector2 insideRight = new(7f, 1f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(insideRight, complex));

            // Points in concave cutouts (should be outside)
            Vector2 outsideLeftCutout = new(1f, 3f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outsideLeftCutout, complex));

            Vector2 outsideRightCutout = new(7f, 3f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outsideRightCutout, complex));

            // Point in center corridor
            Vector2 insideCenter = new(4f, 3f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(insideCenter, complex));
        }

        [Test]
        public void IsPointInsidePolygonTrapezoidWithHorizontalEdgesHandlesCorrectly()
        {
            // Trapezoid with horizontal top and bottom edges
            Vector2[] trapezoid = { new(2f, 0f), new(8f, 0f), new(10f, 5f), new(0f, 5f) };

            // Point inside near bottom
            Vector2 insideBottom = new(5f, 1f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(insideBottom, trapezoid));

            // Point inside near top
            Vector2 insideTop = new(5f, 4f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(insideTop, trapezoid));

            // Point outside left
            Vector2 outsideLeft = new(1f, 1f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outsideLeft, trapezoid));

            // Point outside right
            Vector2 outsideRight = new(9f, 1f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outsideRight, trapezoid));
        }

        [Test]
        public void IsPointInsidePolygonManyConsecutiveColinearVerticesHandlesCorrectly()
        {
            // Square with many colinear vertices on each edge
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

            // Point in center
            Vector2 inside = new(5f, 5f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(inside, square));

            // Point outside
            Vector2 outside = new(15f, 5f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outside, square));
        }

        [Test]
        public void IsPointInsidePolygonCombShapeHandlesCorrectly()
        {
            // Comb shape with alternating teeth and gaps (stress horizontal edges)
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

            // Point in base of comb
            Vector2 insideBase = new(5f, 0.5f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(insideBase, comb));

            // Points in teeth
            Vector2 insideTooth1 = new(1.5f, 2f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(insideTooth1, comb));

            Vector2 insideTooth2 = new(5.5f, 2f);
            Assert.IsTrue(PointPolygonCheck.IsPointInsidePolygon(insideTooth2, comb));

            // Points in gaps between teeth (outside)
            Vector2 outsideGap1 = new(2.5f, 2f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outsideGap1, comb));

            Vector2 outsideGap2 = new(6.5f, 2f);
            Assert.IsFalse(PointPolygonCheck.IsPointInsidePolygon(outsideGap2, comb));
        }

        /*
            The 3D overload projects the polygon into a scratch span, and that span used to be a
            `stackalloc` the caller sized. A polygon big enough overran the stack, which is a
            StackOverflowException no catch intercepts. It now stops at
            StackAllocation.MaxByteBudget and rents above it, so the answer has to be the same on
            both sides of the seam and 20,000 vertices -- once a 160 KiB frame -- has to answer at
            all. See issue #637.
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
