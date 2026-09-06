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
            int bucketSize
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
                    QuadTree2D<int> directQuad = new(entries, bucketSize: bucketSize);
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
