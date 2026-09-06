// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

    [TestFixture]
    [Category("Fast")]
    public sealed class SpatialTreeStoredGeometryTests
    {
        private static IEnumerable<TestCaseData> InvalidGeometryCases()
        {
            string[] variants =
            {
                "Kd2Balanced",
                "Kd2Unbalanced",
                "Quad",
                "QuadEntries",
                "R2",
                "Kd3Balanced",
                "Kd3Unbalanced",
                "Oct",
                "R3",
            };
            float[] invalidValues =
            {
                float.PositiveInfinity,
                float.NegativeInfinity,
                float.NaN,
                -1f,
            };
            int[] bucketSizes = { 1, 32 };
            int[] finiteCounts = { 0, 1, 12 };
            foreach (string variant in variants)
            {
                int dimensions = IsThreeDimensional(variant) || variant == "R2" ? 3 : 2;
                foreach (float invalidValue in invalidValues)
                {
                    for (int axis = 0; axis < dimensions; ++axis)
                    {
                        foreach (int bucketSize in bucketSizes)
                        {
                            foreach (int finiteCount in finiteCounts)
                            {
                                int[] insertionIndices =
                                    finiteCount == 0 ? new[] { 0 }
                                    : finiteCount == 1 ? new[] { 0, 1 }
                                    : new[] { 0, finiteCount / 2, finiteCount };
                                foreach (int insertionIndex in insertionIndices)
                                {
                                    if (!float.IsFinite(invalidValue))
                                    {
                                        yield return new TestCaseData(
                                            variant,
                                            axis,
                                            invalidValue,
                                            bucketSize,
                                            finiteCount,
                                            insertionIndex,
                                            false
                                        );
                                    }
                                    if (variant == "R2" || variant == "R3")
                                    {
                                        yield return new TestCaseData(
                                            variant,
                                            axis,
                                            invalidValue,
                                            bucketSize,
                                            finiteCount,
                                            insertionIndex,
                                            true
                                        );
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        [TestCaseSource(nameof(InvalidGeometryCases))]
        [Timeout(10000)]
        public void InvalidStoredGeometryPreservesFiniteSourceIdentities(
            string variant,
            int axis,
            float invalidValue,
            int bucketSize,
            int finiteCount,
            int insertionIndex,
            bool invalidSize
        )
        {
            int[] source = new int[finiteCount + 1];
            Vector3[] positions = new Vector3[source.Length];
            Bounds[] bounds = new Bounds[source.Length];
            List<int> expected = new();
            for (int index = 0; index < source.Length; ++index)
            {
                source[index] = index;
                Vector3 position = Vector3.zero;
                Vector3 size = Vector3.zero;
                if (index == insertionIndex)
                {
                    if (invalidSize)
                    {
                        size[axis] = invalidValue;
                    }
                    else
                    {
                        position[axis] = invalidValue;
                    }
                }
                else
                {
                    if (1 < finiteCount)
                    {
                        int dimensions = IsThreeDimensional(variant) ? 3 : 2;
                        position[(index / 2) % dimensions] = index % 2 == 0 ? -1f : 1f;
                    }
                    expected.Add(index);
                }
                positions[index] = position;
                bounds[index] = new Bounds(position, size);
            }

            object tree = CreateTree(variant, source, positions, bounds, bucketSize);
            List<int> actual = new() { -1 };
            Bounds finiteQuery = new(Vector3.zero, Vector3.one * 4f);
            Bounds infiniteQuery = new(
                Vector3.zero,
                new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity)
            );
            if (tree is ISpatialTree2D<int> tree2D)
            {
                tree2D.GetElementsInRange(Vector2.zero, 1f, actual);
                CollectionAssert.AreEquivalent(expected, actual, "Finite radius");
                tree2D.GetElementsInRange(Vector2.zero, float.PositiveInfinity, actual);
                CollectionAssert.AreEquivalent(expected, actual, "Infinite radius");
                tree2D.GetElementsInBounds(finiteQuery, actual);
                CollectionAssert.AreEquivalent(expected, actual, "Finite query bounds");
                tree2D.GetElementsInBounds(infiniteQuery, actual);
                CollectionAssert.AreEquivalent(expected, actual, "Infinite query bounds");
                tree2D.GetApproximateNearestNeighbors(Vector2.zero, source.Length + 1, actual);
            }
            else
            {
                ISpatialTree3D<int> tree3D = (ISpatialTree3D<int>)tree;
                tree3D.GetElementsInRange(Vector3.zero, 1f, actual);
                CollectionAssert.AreEquivalent(expected, actual, "Finite radius");
                tree3D.GetElementsInRange(Vector3.zero, float.PositiveInfinity, actual);
                CollectionAssert.AreEquivalent(expected, actual, "Infinite radius");
                tree3D.GetElementsInBounds(finiteQuery, actual);
                CollectionAssert.AreEquivalent(expected, actual, "Finite query bounds");
                tree3D.GetElementsInBounds(infiniteQuery, actual);
                CollectionAssert.AreEquivalent(expected, actual, "Infinite query bounds");
                tree3D.GetApproximateNearestNeighbors(Vector3.zero, source.Length + 1, actual);
            }
            CollectionAssert.AreEqual(
                expected,
                actual,
                "Nearest preserves insertion order for equidistant elements"
            );
        }

        [TestCase("R2", 0)]
        [TestCase("R2", 1)]
        [TestCase("R2", 2)]
        [TestCase("R3", 0)]
        [TestCase("R3", 1)]
        [TestCase("R3", 2)]
        public void OverflowedStoredBoundsExcludeOnlyTheirSourceEntry(string variant, int axis)
        {
            int[] source = { 0, 1, 2 };
            Vector3[] positions = { Vector3.zero, Vector3.zero, Vector3.zero };
            Vector3 center = Vector3.zero;
            center[axis] = float.MaxValue;
            Vector3 size = Vector3.zero;
            size[axis] = float.MaxValue;
            Bounds[] bounds =
            {
                new(Vector3.zero, Vector3.zero),
                new(center, size),
                new(Vector3.zero, Vector3.zero),
            };
            Assert.IsTrue(
                float.IsPositiveInfinity(bounds[1].max[axis]),
                "The stored edge must overflow"
            );
            object tree = CreateTree(variant, source, positions, bounds, 1);
            List<int> actual = new() { -1 };
            Bounds query = new(Vector3.zero, Vector3.one);
            if (tree is RTree2D<int> r2)
            {
                r2.GetElementsWithCentersInBounds(query, actual);
            }
            else
            {
                ((RTree3D<int>)tree).GetElementsWithCentersInBounds(query, actual);
            }
            CollectionAssert.AreEquivalent(new[] { 0, 2 }, actual);
        }

        [Test]
        public void ExplicitInfiniteOctTreeBoundaryPreservesFinitePoints()
        {
            Vector3[] points = { Vector3.left, new(float.PositiveInfinity, 0f, 0f), Vector3.right };
            Bounds boundary = new(
                Vector3.zero,
                new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity)
            );
            OctTree3D<Vector3> tree = new(points, static point => point, boundary, bucketSize: 1);
            List<Vector3> actual = new();
            tree.GetElementsInRange(Vector3.zero, float.PositiveInfinity, actual);
            CollectionAssert.AreEquivalent(new[] { Vector3.left, Vector3.right }, actual);
            tree.GetElementsInBounds(boundary, actual);
            CollectionAssert.AreEquivalent(new[] { Vector3.left, Vector3.right }, actual);
        }

        private static IEnumerable<TestCaseData> FiniteExtremeCases()
        {
            string[] variants =
            {
                "Kd2Balanced",
                "Kd2Unbalanced",
                "Quad",
                "QuadEntries",
                "R2",
                "Kd3Balanced",
                "Kd3Unbalanced",
                "Oct",
                "R3",
            };
            int[] bucketSizes = { 1, 32 };
            int[] signs = { -1, 1 };
            foreach (string variant in variants)
            {
                int dimensions = IsThreeDimensional(variant) ? 3 : 2;
                for (int axis = 0; axis < dimensions; ++axis)
                {
                    foreach (int bucketSize in bucketSizes)
                    {
                        foreach (int sign in signs)
                        {
                            for (int layout = 0; layout < 7; ++layout)
                            {
                                int boundaryCount =
                                    variant == "Quad"
                                    || variant == "QuadEntries"
                                    || variant == "Oct"
                                        ? 3
                                        : 1;
                                for (
                                    int boundaryKind = 0;
                                    boundaryKind < boundaryCount;
                                    ++boundaryKind
                                )
                                {
                                    yield return new TestCaseData(
                                        variant,
                                        axis,
                                        bucketSize,
                                        sign,
                                        layout,
                                        boundaryKind
                                    );
                                }
                            }
                        }
                    }
                }
            }
        }

        [TestCaseSource(nameof(FiniteExtremeCases))]
        [Timeout(10000)]
        public void FiniteExtremesMatchIndependentQueryOracles(
            string variant,
            int axis,
            int bucketSize,
            int sign,
            int layout,
            int boundaryKind
        )
        {
            float extreme = sign * float.MaxValue;
            float adjacent =
                sign
                * BitConverter.Int32BitsToSingle(
                    BitConverter.SingleToInt32Bits(float.MaxValue) - 1
                );
            float[] coordinates;
            switch (layout)
            {
                case 0:
                    coordinates = new[] { extreme, extreme };
                    break;
                case 1:
                    coordinates = new[] { extreme, adjacent, extreme, adjacent };
                    break;
                case 2:
                    coordinates = new[] { extreme, extreme * 0.5f, extreme * 0.75f, extreme };
                    break;
                case 3:
                    coordinates = new[] { extreme, -extreme, 0f, adjacent, -adjacent, extreme };
                    break;
                case 4:
                    coordinates = new[] { extreme * 0.5f, -extreme * 0.5f, 0f, extreme * 0.5f };
                    break;
                case 5:
                    coordinates = new[] { -sign, sign * 1e30f, -sign, 0f };
                    break;
                default:
                    coordinates = new[] { (float)sign, sign * 1e30f };
                    break;
            }
            int[] source = new int[coordinates.Length];
            Vector3[] positions = new Vector3[source.Length];
            Bounds[] storedBounds = new Bounds[source.Length];
            bool boxTree = variant == "R2" || variant == "R3";
            for (int index = 0; index < source.Length; ++index)
            {
                source[index] = index;
                Vector3 position = Vector3.zero;
                position[axis] = coordinates[index];
                positions[index] = position;
                Vector3 extent = Vector3.zero;
                if (layout == 4 && boxTree && coordinates[index] != 0f)
                {
                    extent[axis] = float.MaxValue * 0.5f;
                }
                Bounds bounds = new(position, Vector3.zero);
                bounds.extents = extent;
                storedBounds[index] = bounds;
                Assert.IsTrue(float.IsFinite(bounds.min[axis]) && float.IsFinite(bounds.max[axis]));
            }
            Bounds? suppliedBoundary =
                boundaryKind == 0 ? null
                : boundaryKind == 1 ? new Bounds(positions[0], Vector3.zero)
                : new Bounds(
                    Vector3.zero,
                    new Vector3(
                        float.PositiveInfinity,
                        float.PositiveInfinity,
                        float.PositiveInfinity
                    )
                );
            object tree = CreateTree(
                variant,
                source,
                positions,
                storedBounds,
                bucketSize,
                suppliedBoundary
            );
            int dimensions = IsThreeDimensional(variant) ? 3 : 2;
            Bounds treeBoundary = tree is ISpatialTree2D<int> boundary2D
                ? boundary2D.Boundary
                : ((ISpatialTree3D<int>)tree).Boundary;
            for (int dimension = 0; dimension < dimensions; ++dimension)
            {
                Assert.IsTrue(float.IsFinite(treeBoundary.center[dimension]), "Finite node center");
                foreach (int index in source)
                {
                    float minimum = boxTree
                        ? storedBounds[index].min[dimension]
                        : positions[index][dimension];
                    float maximum = boxTree
                        ? storedBounds[index].max[dimension]
                        : positions[index][dimension];
                    Assert.IsTrue(
                        treeBoundary.min[dimension] <= minimum
                            && maximum <= treeBoundary.max[dimension],
                        "Boundary encloses source geometry"
                    );
                }
            }
            List<int> actual = new();
            List<int> expected = new();
            float[] radii =
            {
                0f,
                1f,
                Math.Abs(extreme - adjacent),
                float.MaxValue,
                float.PositiveInfinity,
            };
            List<Vector3> queries = new(positions) { Vector3.zero };
            Vector3 edgeQuery = Vector3.zero;
            edgeQuery[axis] = extreme;
            queries.Add(edgeQuery);
            foreach (Vector3 query in queries)
            {
                foreach (float radius in radii)
                {
                    foreach (float minimumRadius in new[] { 0f, radius * 0.5f })
                    {
                        expected.Clear();
                        foreach (int index in source)
                        {
                            double distance = OracleDistanceSquared(
                                query,
                                positions[index],
                                storedBounds[index],
                                dimensions,
                                boxTree
                            );
                            if (
                                distance <= (double)radius * radius
                                && !(
                                    0f < minimumRadius
                                    && distance <= (double)minimumRadius * minimumRadius
                                )
                            )
                            {
                                expected.Add(index);
                            }
                        }
                        actual.Add(-1);
                        if (tree is ISpatialTree2D<int> range2D)
                        {
                            range2D.GetElementsInRange(query, radius, actual, minimumRadius);
                        }
                        else
                        {
                            ((ISpatialTree3D<int>)tree).GetElementsInRange(
                                query,
                                radius,
                                actual,
                                minimumRadius
                            );
                        }
                        CollectionAssert.AreEquivalent(
                            expected,
                            actual,
                            $"Radius {radius}, minimum {minimumRadius}, query {query}"
                        );
                    }
                }
                Bounds[] queryBounds =
                {
                    new(query, Vector3.zero),
                    new(query, Vector3.one),
                    new(
                        Vector3.zero,
                        new Vector3(
                            float.PositiveInfinity,
                            float.PositiveInfinity,
                            float.PositiveInfinity
                        )
                    ),
                };
                foreach (Bounds bounds in queryBounds)
                {
                    expected.Clear();
                    foreach (int index in source)
                    {
                        bool matches = true;
                        for (int dimension = 0; dimension < dimensions; ++dimension)
                        {
                            float minimum = boxTree
                                ? storedBounds[index].min[dimension]
                                : positions[index][dimension];
                            float maximum = boxTree
                                ? storedBounds[index].max[dimension]
                                : positions[index][dimension];
                            matches &=
                                minimum <= bounds.max[dimension]
                                && bounds.min[dimension] <= maximum;
                        }
                        if (matches)
                        {
                            expected.Add(index);
                        }
                    }
                    if (tree is ISpatialTree2D<int> bounds2D)
                    {
                        bounds2D.GetElementsInBounds(bounds, actual);
                    }
                    else
                    {
                        ((ISpatialTree3D<int>)tree).GetElementsInBounds(bounds, actual);
                    }
                    CollectionAssert.AreEquivalent(
                        expected,
                        actual,
                        $"Bounds {bounds}, query {query}"
                    );
                }
                expected.Clear();
                expected.AddRange(source);
                expected.Sort(
                    (left, right) =>
                    {
                        double leftDistance = OracleDistanceSquared(
                            query,
                            positions[left],
                            default,
                            dimensions,
                            false
                        );
                        double rightDistance = OracleDistanceSquared(
                            query,
                            positions[right],
                            default,
                            dimensions,
                            false
                        );
                        int order = leftDistance.CompareTo(rightDistance);
                        return order == 0 ? left.CompareTo(right) : order;
                    }
                );
                int[] neighborCounts =
                    variant == "Oct" || boxTree
                        ? new[] { 1, 2, source.Length + 1 }
                        : new[] { source.Length + 1 };
                foreach (int neighborCount in neighborCounts)
                {
                    if (tree is ISpatialTree2D<int> nearest2D)
                    {
                        nearest2D.GetApproximateNearestNeighbors(query, neighborCount, actual);
                    }
                    else
                    {
                        ((ISpatialTree3D<int>)tree).GetApproximateNearestNeighbors(
                            query,
                            neighborCount,
                            actual
                        );
                    }
                    Assert.AreEqual(Math.Min(neighborCount, source.Length), actual.Count);
                    CollectionAssert.IsSubsetOf(actual, source);
                    CollectionAssert.AllItemsAreUnique(actual);
                    if (source.Length <= neighborCount)
                    {
                        CollectionAssert.AreEqual(expected, actual, $"Nearest order at {query}");
                    }
                    else
                    {
                        for (int index = 0; index < actual.Count; ++index)
                        {
                            double expectedDistance = OracleDistanceSquared(
                                query,
                                positions[expected[index]],
                                default,
                                dimensions,
                                false
                            );
                            double actualDistance = OracleDistanceSquared(
                                query,
                                positions[actual[index]],
                                default,
                                dimensions,
                                false
                            );
                            Assert.AreEqual(
                                expectedDistance,
                                actualDistance,
                                $"Nearest distance {index} at {query}"
                            );
                            if (0 < index)
                            {
                                double previousDistance = OracleDistanceSquared(
                                    query,
                                    positions[actual[index - 1]],
                                    default,
                                    dimensions,
                                    false
                                );
                                Assert.IsTrue(
                                    previousDistance < actualDistance
                                        || (
                                            previousDistance == actualDistance
                                            && actual[index - 1] < actual[index]
                                        ),
                                    "Selected ties retain source order"
                                );
                            }
                        }
                    }
                }
            }
        }

        private static double OracleDistanceSquared(
            Vector3 query,
            Vector3 point,
            Bounds bounds,
            int dimensions,
            bool box
        )
        {
            double sum = 0d;
            for (int axis = 0; axis < dimensions; ++axis)
            {
                double coordinate = point[axis];
                if (box)
                {
                    coordinate = Math.Max(
                        bounds.min[axis],
                        Math.Min((double)query[axis], bounds.max[axis])
                    );
                }
                double difference = (double)query[axis] - coordinate;
                sum += difference * difference;
            }
            return sum;
        }

        private static bool IsThreeDimensional(string variant)
        {
            return variant == "Kd3Balanced"
                || variant == "Kd3Unbalanced"
                || variant == "Oct"
                || variant == "R3";
        }

        private static object CreateTree(
            string variant,
            int[] source,
            Vector3[] positions,
            Bounds[] bounds,
            int bucketSize,
            Bounds? boundary = null
        )
        {
            switch (variant)
            {
                case "Kd2Balanced":
                case "Kd2Unbalanced":
                    KdTree2D<int> kd2 = new(
                        source,
                        index => positions[index],
                        bucketSize,
                        variant == "Kd2Balanced"
                    );
                    CollectionAssert.AreEqual(source, kd2.elements, "Source snapshot");
                    return kd2;
                case "Quad":
                    QuadTree2D<int> quad = new(
                        source,
                        index => positions[index],
                        boundary,
                        bucketSize: bucketSize
                    );
                    CollectionAssert.AreEqual(source, quad.elements, "Source snapshot");
                    return quad;
                case "QuadEntries":
                    List<QuadTree2D<int>.Entry> entries = new();
                    foreach (int index in source)
                    {
                        entries.Add(new QuadTree2D<int>.Entry(index, positions[index]));
                    }
                    QuadTree2D<int> directQuad = new(entries, boundary, bucketSize: bucketSize);
                    CollectionAssert.AreEqual(source, directQuad.elements, "Source snapshot");
                    return directQuad;
                case "R2":
                    RTree2D<int> r2 = new(source, index => bounds[index], bucketSize);
                    CollectionAssert.AreEqual(source, r2.elements, "Source snapshot");
                    return r2;
                case "Kd3Balanced":
                case "Kd3Unbalanced":
                    KdTree3D<int> kd3 = new(
                        source,
                        index => positions[index],
                        bucketSize,
                        variant == "Kd3Balanced"
                    );
                    CollectionAssert.AreEqual(source, kd3.elements, "Source snapshot");
                    return kd3;
                case "Oct":
                    OctTree3D<int> oct = new(
                        source,
                        index => positions[index],
                        boundary,
                        bucketSize: bucketSize
                    );
                    CollectionAssert.AreEqual(source, oct.elements, "Source snapshot");
                    return oct;
                case "R3":
                    RTree3D<int> r3 = new(source, index => bounds[index], bucketSize);
                    CollectionAssert.AreEqual(source, r3.elements, "Source snapshot");
                    return r3;
                default:
                    throw new ArgumentOutOfRangeException(nameof(variant));
            }
        }
    }
}
