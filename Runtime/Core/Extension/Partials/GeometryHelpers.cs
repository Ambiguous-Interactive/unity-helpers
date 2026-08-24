// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using DataStructure.Adapters;
    using Helper;
    using UnityEngine;
    using Utils;
#if UNITY_EDITOR
#endif

    /// <summary>
    /// Provides extension methods for Unity types including GameObject, Transform, Bounds, Rect,
    /// Vector types, UI components, and advanced geometric algorithms such as convex/concave hull generation.
    /// </summary>
    /// <remarks>
    /// Thread Safety: Most methods require execution on the Unity main thread due to Unity API calls.
    /// Performance: Methods use object pooling where possible to minimize allocations.
    /// This class contains geometric algorithms adapted from various sources (see method-level comments).
    /// </remarks>
    public static partial class UnityExtensions
    {
        private const float ConvexHullRelationEpsilon = 1e-5f;
        private const double ConvexHullOrientationEpsilon = 1e-8d;

        private static readonly Comparison<Vector2> Vector2LexicographicalComparison = (lhs, rhs) =>
        {
            int cmp = lhs.x.CompareTo(rhs.x);
            return cmp != 0 ? cmp : lhs.y.CompareTo(rhs.y);
        };

        private static double ComputeAreaTolerance(Vector2 a, Vector2 b, Vector2 c)
        {
            double maxComponent = Math.Max(
                Math.Max(
                    Math.Max(Math.Abs(a.x), Math.Abs(a.y)),
                    Math.Max(Math.Abs(b.x), Math.Abs(b.y))
                ),
                Math.Max(Math.Abs(c.x), Math.Abs(c.y))
            );
            double scale = Math.Max(1d, maxComponent);
            return ConvexHullRelationEpsilon * scale * scale;
        }

        private static bool AreApproximatelyColinear(Vector2 a, Vector2 b, Vector2 c)
        {
            double cross = Geometry.IsAPointLeftOfVectorOrOnTheLineDouble(a, b, c);
            double tolerance = ComputeAreaTolerance(a, b, c);
            return Math.Abs(cross) <= tolerance;
        }

        private static bool AreVector2PointsEquivalent(Vector2 lhs, Vector2 rhs)
        {
            return Mathf.Abs(lhs.x - rhs.x) <= ConvexHullRelationEpsilon
                && Mathf.Abs(lhs.y - rhs.y) <= ConvexHullRelationEpsilon;
        }

        private static void DeduplicateSortedVector2(List<Vector2> points)
        {
            if (points == null || points.Count <= 1)
            {
                return;
            }

            int writeIndex = 1;
            for (int readIndex = 1; readIndex < points.Count; ++readIndex)
            {
                Vector2 previous = points[writeIndex - 1];
                Vector2 current = points[readIndex];
                if (!AreVector2PointsEquivalent(previous, current))
                {
                    if (writeIndex != readIndex)
                    {
                        points[writeIndex] = current;
                    }
                    ++writeIndex;
                }
            }

            if (writeIndex < points.Count)
            {
                points.RemoveRange(writeIndex, points.Count - writeIndex);
            }
        }

        private static void DeduplicateSortedVector3Int(List<Vector3Int> points)
        {
            if (points == null || points.Count <= 1)
            {
                return;
            }

            int writeIndex = 1;
            for (int readIndex = 1; readIndex < points.Count; ++readIndex)
            {
                Vector3Int previous = points[writeIndex - 1];
                Vector3Int current = points[readIndex];
                if (previous != current)
                {
                    if (writeIndex != readIndex)
                    {
                        points[writeIndex] = current;
                    }
                    ++writeIndex;
                }
            }

            if (writeIndex < points.Count)
            {
                points.RemoveRange(writeIndex, points.Count - writeIndex);
            }
        }

        private static void DeduplicateSortedFastVector3Int(List<FastVector3Int> points)
        {
            if (points == null || points.Count <= 1)
            {
                return;
            }

            int writeIndex = 1;
            for (int readIndex = 1; readIndex < points.Count; ++readIndex)
            {
                FastVector3Int previous = points[writeIndex - 1];
                FastVector3Int current = points[readIndex];
                if (previous != current)
                {
                    if (writeIndex != readIndex)
                    {
                        points[writeIndex] = current;
                    }
                    ++writeIndex;
                }
            }

            if (writeIndex < points.Count)
            {
                points.RemoveRange(writeIndex, points.Count - writeIndex);
            }
        }

        public enum ConvexHullAlgorithm
        {
            [Obsolete("Do not use default value; specify an algorithm explicitly.")]
            Unknown = 0,
            MonotoneChain = 1,
            Jarvis = 2,
        }

        private static int FindLowestGridPointIndex(List<FastVector3Int> points, Grid grid)
        {
            if (points == null || points.Count == 0)
            {
                return -1;
            }

            if (grid == null)
            {
                throw new ArgumentNullException(nameof(grid));
            }

            int lowestIndex = -1;
            float lowestY = float.MaxValue;
            for (int i = 0; i < points.Count; ++i)
            {
                Vector2 candidateWorld = grid.CellToWorld(points[i]);
                if (lowestIndex < 0 || candidateWorld.y < lowestY)
                {
                    lowestIndex = i;
                    lowestY = candidateWorld.y;
                }
            }

            return lowestIndex;
        }

        private static bool TryBuildGridConcaveHullAttempt(
            List<FastVector3Int> points,
            List<FastVector3Int> hull,
            Grid grid,
            bool[] availability,
            List<int> neighborIndices,
            float[] neighborDistances,
            Vector2[] worldPositions,
            int nearestNeighbors,
            int firstIndex,
            FastVector3Int firstPoint,
            int maxSteps
        )
        {
            int totalPoints = points.Count;
            int availableCount = ResetAvailabilityFlags(availability, totalPoints, firstIndex);

            int step = 2;
            float previousAngle = 0f;
            Vector2 currentWorld = worldPositions[firstIndex];
            bool includeFirstCandidate = false;

            while (0 < availableCount)
            {
                if (!includeFirstCandidate && step == 5)
                {
                    includeFirstCandidate = true;
                }

                FillNearestNeighborIndices(
                    availability,
                    neighborIndices,
                    neighborDistances,
                    worldPositions,
                    totalPoints,
                    nearestNeighbors,
                    currentWorld,
                    includeFirstCandidate,
                    firstIndex
                );

                if (neighborIndices.Count == 0)
                {
                    break;
                }

                SortByRightHandTurnIndices(
                    neighborIndices,
                    worldPositions,
                    currentWorld,
                    previousAngle
                );

                bool intersects = true;
                int neighborOffset = -1;
                while (intersects && neighborOffset < neighborIndices.Count - 1)
                {
                    ++neighborOffset;
                    int candidateIndex = neighborIndices[neighborOffset];
                    FastVector3Int candidate = points[candidateIndex];
                    int lastPoint = candidate == firstPoint ? 1 : 0;
                    int j = 2;
                    intersects = false;
                    Vector2 lhsTo = worldPositions[candidateIndex];
                    while (!intersects && j < hull.Count - lastPoint)
                    {
                        Vector2 lhsFrom = grid.CellToWorld(hull[step - 2]);
                        Vector2 rhsFrom = grid.CellToWorld(hull[step - 2 - j]);
                        Vector2 rhsTo = grid.CellToWorld(hull[step - 1 - j]);
                        intersects = Intersects(lhsFrom, lhsTo, rhsFrom, rhsTo);
                        ++j;
                    }
                }

                if (intersects)
                {
                    return RemainingPointsInside(points, availability, hull, grid);
                }

                int nextIndex = neighborIndices[neighborOffset];
                if (nextIndex == firstIndex)
                {
                    break;
                }

                FastVector3Int nextPoint = points[nextIndex];
                hull.Add(nextPoint);
                if (availability[nextIndex])
                {
                    availability[nextIndex] = false;
                    --availableCount;
                }

                currentWorld = worldPositions[nextIndex];
                previousAngle = CalculateAngle(
                    grid.CellToWorld(hull[step - 1]),
                    grid.CellToWorld(hull[step - 2])
                );
                ++step;
                if (maxSteps < step)
                {
                    break;
                }
            }

            return RemainingPointsInside(points, availability, hull, grid);
        }

        private static void FillNearestNeighborIndices(
            bool[] availability,
            List<int> neighborIndices,
            float[] neighborDistances,
            Vector2[] worldPositions,
            int totalPoints,
            int nearestNeighbors,
            Vector2 currentWorld,
            bool includeFirstCandidate,
            int firstIndex
        )
        {
            neighborIndices.Clear();
            if (nearestNeighbors <= 0 || worldPositions == null || totalPoints <= 0)
            {
                return;
            }

            int storedCount = 0;
            for (int i = 0; i < totalPoints; ++i)
            {
                if (!availability[i])
                {
                    continue;
                }

                Vector2 candidateWorld = worldPositions[i];
                float distance = (candidateWorld - currentWorld).sqrMagnitude;
                InsertNeighborCandidate(
                    neighborIndices,
                    neighborDistances,
                    i,
                    distance,
                    ref storedCount,
                    nearestNeighbors
                );
            }

            if (includeFirstCandidate)
            {
                Vector2 firstWorld = worldPositions[firstIndex];
                float firstDistance = (firstWorld - currentWorld).sqrMagnitude;
                InsertNeighborCandidate(
                    neighborIndices,
                    neighborDistances,
                    firstIndex,
                    firstDistance,
                    ref storedCount,
                    nearestNeighbors
                );
            }
        }

        private static void SortByRightHandTurnIndices(
            List<int> neighborIndices,
            Vector2[] worldPositions,
            Vector2 currentWorld,
            float previousAngle
        )
        {
            int count = neighborIndices.Count;
            if (count <= 1)
            {
                return;
            }

            using PooledArray<float> angleBufferResource = SystemArrayPool<float>.Get(
                count,
                out float[] angles
            );
            for (int i = 0; i < count; ++i)
            {
                Vector2 candidatePoint = worldPositions[neighborIndices[i]];
                float candidateAngle = CalculateAngle(currentWorld, candidatePoint);
                angles[i] = -AngleDifference(previousAngle, candidateAngle);
            }

            SelectionSort(neighborIndices, angles, count);
        }

        private static bool RemainingPointsInside(
            List<FastVector3Int> points,
            bool[] availability,
            List<FastVector3Int> hull,
            Grid grid
        )
        {
            for (int i = 0; i < points.Count; ++i)
            {
                if (!availability[i])
                {
                    continue;
                }

                if (!IsPositionInside(hull, points[i], grid))
                {
                    return false;
                }
            }

            return true;
        }

        private static int FindLowestPointIndex(List<Vector2> points)
        {
            if (points == null || points.Count == 0)
            {
                return -1;
            }

            int lowestIndex = -1;
            float lowestY = float.MaxValue;
            Vector2 lowestPoint = default;
            for (int i = 0; i < points.Count; ++i)
            {
                Vector2 candidate = points[i];
                if (
                    lowestIndex < 0
                    || candidate.y < lowestY
                    || (Mathf.Approximately(candidate.y, lowestY) && candidate.x < lowestPoint.x)
                )
                {
                    lowestIndex = i;
                    lowestY = candidate.y;
                    lowestPoint = candidate;
                }
            }

            return lowestIndex;
        }

        private static bool TryBuildConcaveHull2Attempt(
            List<Vector2> points,
            List<Vector2> hull,
            bool[] availability,
            List<int> neighborIndices,
            float[] neighborDistances,
            int nearestNeighbors,
            int firstIndex,
            Vector2 firstPoint,
            int maxSteps
        )
        {
            int totalPoints = points.Count;
            int availableCount = ResetAvailabilityFlags(availability, totalPoints, firstIndex);

            int step = 2;
            float previousAngle = 0f;
            Vector2 currentPoint = firstPoint;
            bool includeFirstCandidate = false;

            while (0 < availableCount)
            {
                if (!includeFirstCandidate && step == 5)
                {
                    includeFirstCandidate = true;
                }

                FillNearestNeighborIndices(
                    points,
                    availability,
                    neighborIndices,
                    neighborDistances,
                    nearestNeighbors,
                    currentPoint,
                    includeFirstCandidate,
                    firstIndex
                );

                if (neighborIndices.Count == 0)
                {
                    break;
                }

                SortByRightHandTurnIndices(neighborIndices, points, currentPoint, previousAngle);

                bool intersects = true;
                int neighborOffset = -1;
                while (intersects && neighborOffset < neighborIndices.Count - 1)
                {
                    ++neighborOffset;
                    Vector2 candidate = points[neighborIndices[neighborOffset]];
                    int lastPoint = candidate == firstPoint ? 1 : 0;
                    int j = 2;
                    intersects = false;
                    Vector2 lhsTo = candidate;
                    while (!intersects && j < hull.Count - lastPoint)
                    {
                        Vector2 lhsFrom = hull[step - 2];
                        Vector2 rhsFrom = hull[step - 2 - j];
                        Vector2 rhsTo = hull[step - 1 - j];
                        intersects = Intersects(lhsFrom, lhsTo, rhsFrom, rhsTo);
                        ++j;
                    }
                }

                if (intersects)
                {
                    return RemainingPointsInside(points, availability, hull);
                }

                int nextIndex = neighborIndices[neighborOffset];
                if (nextIndex == firstIndex)
                {
                    break;
                }

                Vector2 nextPoint = points[nextIndex];
                hull.Add(nextPoint);
                if (availability[nextIndex])
                {
                    availability[nextIndex] = false;
                    --availableCount;
                }

                currentPoint = nextPoint;
                previousAngle = CalculateAngle(hull[step - 1], hull[step - 2]);
                ++step;
                if (maxSteps < step)
                {
                    break;
                }
            }

            return RemainingPointsInside(points, availability, hull);
        }

        private static void FillNearestNeighborIndices(
            List<Vector2> points,
            bool[] availability,
            List<int> neighborIndices,
            float[] neighborDistances,
            int nearestNeighbors,
            Vector2 currentPoint,
            bool includeFirstCandidate,
            int firstIndex
        )
        {
            neighborIndices.Clear();
            if (nearestNeighbors <= 0 || points.Count == 0)
            {
                return;
            }

            int storedCount = 0;
            int totalPoints = points.Count;
            for (int i = 0; i < totalPoints; ++i)
            {
                if (!availability[i])
                {
                    continue;
                }

                Vector2 candidate = points[i];
                float distance = (candidate - currentPoint).sqrMagnitude;
                InsertNeighborCandidate(
                    neighborIndices,
                    neighborDistances,
                    i,
                    distance,
                    ref storedCount,
                    nearestNeighbors
                );
            }

            if (includeFirstCandidate)
            {
                Vector2 firstPoint = points[firstIndex];
                float firstDistance = (firstPoint - currentPoint).sqrMagnitude;
                InsertNeighborCandidate(
                    neighborIndices,
                    neighborDistances,
                    firstIndex,
                    firstDistance,
                    ref storedCount,
                    nearestNeighbors
                );
            }
        }

        private static void SortByRightHandTurnIndices(
            List<int> neighborIndices,
            List<Vector2> points,
            Vector2 currentPoint,
            float previousAngle
        )
        {
            int count = neighborIndices.Count;
            if (count <= 1)
            {
                return;
            }

            using PooledArray<float> angleBufferResource = SystemArrayPool<float>.Get(
                count,
                out float[] angles
            );
            for (int i = 0; i < count; ++i)
            {
                Vector2 candidatePoint = points[neighborIndices[i]];
                float candidateAngle = CalculateAngle(currentPoint, candidatePoint);
                angles[i] = -AngleDifference(previousAngle, candidateAngle);
            }

            SelectionSort(neighborIndices, angles, count);
        }

        private static bool RemainingPointsInside(
            List<Vector2> points,
            bool[] availability,
            List<Vector2> hull
        )
        {
            for (int i = 0; i < points.Count; ++i)
            {
                if (!availability[i])
                {
                    continue;
                }

                if (!IsPositionInside(hull, points[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static int ResetAvailabilityFlags(
            bool[] availability,
            int totalPoints,
            int skipIndex
        )
        {
            if (availability == null)
            {
                return 0;
            }

            int availableCount = 0;
            for (int i = 0; i < totalPoints; ++i)
            {
                bool available = i != skipIndex;
                availability[i] = available;
                if (available)
                {
                    ++availableCount;
                }
            }

            return availableCount;
        }

        internal static List<Vector2> BuildConvexHullJarvisFallback(
            List<Vector2> points,
            List<Vector2> hull,
            bool includeColinearPoints,
            List<int> scratchIndices,
            float[] scratchDistances,
            bool[] membershipFlags
        )
        {
            hull ??= new List<Vector2>();
            hull.Clear();
            int pointCount = points?.Count ?? 0;
            if (pointCount == 0 || points == null)
            {
                return hull;
            }

            if (pointCount <= 2)
            {
                hull.AddRange(points);
                return hull;
            }

            ResetBooleanFlags(membershipFlags, pointCount);

            int startIndex = FindLowestPointIndex(points);
            if (startIndex < 0)
            {
                hull.AddRange(points);
                return hull;
            }

            int currentIndex = startIndex;
            int guard = 0;
            int guardMax = Math.Max(8, pointCount * 8);

            do
            {
                Vector2 current = points[currentIndex];
                hull.Add(current);
                if (membershipFlags != null && currentIndex < membershipFlags.Length)
                {
                    membershipFlags[currentIndex] = true;
                }
                if (!includeColinearPoints)
                {
                    TrimTailColinear(hull);
                }

                int candidateIndex = -1;
                for (int i = 0; i < pointCount; ++i)
                {
                    if (i == currentIndex)
                    {
                        continue;
                    }

                    candidateIndex = i;
                    break;
                }

                if (candidateIndex < 0)
                {
                    break;
                }

                for (int i = 0; i < pointCount; ++i)
                {
                    if (i == currentIndex || i == candidateIndex)
                    {
                        continue;
                    }

                    Vector2 candidate = points[candidateIndex];
                    Vector2 point = points[i];
                    float relation = Geometry.IsAPointLeftOfVectorOrOnTheLine(
                        current,
                        candidate,
                        point
                    );
                    if (ConvexHullRelationEpsilon < relation)
                    {
                        candidateIndex = i;
                        continue;
                    }

                    if (Mathf.Abs(relation) <= ConvexHullRelationEpsilon)
                    {
                        float candidateDistance = (candidate - current).sqrMagnitude;
                        float pointDistance = (point - current).sqrMagnitude;
                        if (candidateDistance < pointDistance)
                        {
                            candidateIndex = i;
                        }
                    }
                }

                if (includeColinearPoints && scratchIndices != null)
                {
                    scratchIndices.Clear();
                    for (int i = 0; i < pointCount; ++i)
                    {
                        if (i == currentIndex || i == candidateIndex)
                        {
                            continue;
                        }

                        float relation = Geometry.IsAPointLeftOfVectorOrOnTheLine(
                            current,
                            points[candidateIndex],
                            points[i]
                        );
                        if (Mathf.Abs(relation) <= ConvexHullRelationEpsilon)
                        {
                            scratchIndices.Add(i);
                        }
                    }

                    SortIndicesByDistance(points, current, scratchIndices, scratchDistances);
                    if (0 < scratchIndices.Count)
                    {
                        foreach (int index in scratchIndices)
                        {
                            if (membershipFlags != null && membershipFlags[index])
                            {
                                continue;
                            }

                            hull.Add(points[index]);
                            if (membershipFlags != null && index < membershipFlags.Length)
                            {
                                membershipFlags[index] = true;
                            }
                        }
                    }
                }

                currentIndex = candidateIndex;
                if (guardMax < ++guard)
                {
                    break;
                }
            } while (currentIndex != startIndex);

            if (!includeColinearPoints && 2 < hull.Count)
            {
                PruneColinearOnHull(hull);
            }

            return hull;
        }

        internal static List<FastVector3Int> BuildGridConvexHullJarvisFallback(
            List<FastVector3Int> points,
            Vector2[] worldPositions,
            List<FastVector3Int> hull,
            bool includeColinearPoints,
            List<int> scratchIndices,
            float[] scratchDistances,
            bool[] membershipFlags
        )
        {
            hull ??= new List<FastVector3Int>();
            hull.Clear();
            int pointCount = points?.Count ?? 0;
            if (pointCount == 0 || points == null)
            {
                return hull;
            }

            if (pointCount <= 2)
            {
                hull.AddRange(points);
                return hull;
            }

            ResetBooleanFlags(membershipFlags, pointCount);

            int startIndex = FindLowestWorldPositionIndex(worldPositions, pointCount);
            if (startIndex < 0)
            {
                hull.AddRange(points);
                return hull;
            }

            int currentIndex = startIndex;
            int guard = 0;
            int guardMax = Math.Max(8, pointCount * 8);

            do
            {
                hull.Add(points[currentIndex]);
                if (membershipFlags != null && currentIndex < membershipFlags.Length)
                {
                    membershipFlags[currentIndex] = true;
                }
                if (!includeColinearPoints)
                {
                    TrimTailColinear(hull);
                }

                int candidateIndex = -1;
                for (int i = 0; i < pointCount; ++i)
                {
                    if (i == currentIndex)
                    {
                        continue;
                    }

                    candidateIndex = i;
                    break;
                }

                if (candidateIndex < 0)
                {
                    break;
                }

                for (int i = 0; i < pointCount; ++i)
                {
                    if (i == currentIndex || i == candidateIndex)
                    {
                        continue;
                    }

                    Vector2 candidate = worldPositions[candidateIndex];
                    Vector2 point = worldPositions[i];
                    float relation = Geometry.IsAPointLeftOfVectorOrOnTheLine(
                        worldPositions[currentIndex],
                        candidate,
                        point
                    );
                    if (ConvexHullRelationEpsilon < relation)
                    {
                        candidateIndex = i;
                        continue;
                    }

                    if (Mathf.Abs(relation) <= ConvexHullRelationEpsilon)
                    {
                        float candidateDistance = (
                            candidate - worldPositions[currentIndex]
                        ).sqrMagnitude;
                        float pointDistance = (point - worldPositions[currentIndex]).sqrMagnitude;
                        if (candidateDistance < pointDistance)
                        {
                            candidateIndex = i;
                        }
                    }
                }

                if (includeColinearPoints && scratchIndices != null)
                {
                    scratchIndices.Clear();
                    for (int i = 0; i < pointCount; ++i)
                    {
                        if (i == currentIndex || i == candidateIndex)
                        {
                            continue;
                        }

                        float relation = Geometry.IsAPointLeftOfVectorOrOnTheLine(
                            worldPositions[currentIndex],
                            worldPositions[candidateIndex],
                            worldPositions[i]
                        );
                        if (Mathf.Abs(relation) <= ConvexHullRelationEpsilon)
                        {
                            scratchIndices.Add(i);
                        }
                    }

                    SortIndicesByDistance(
                        worldPositions,
                        worldPositions[currentIndex],
                        scratchIndices,
                        scratchDistances
                    );
                    if (0 < scratchIndices.Count)
                    {
                        foreach (int index in scratchIndices)
                        {
                            if (membershipFlags != null && membershipFlags[index])
                            {
                                continue;
                            }

                            hull.Add(points[index]);
                            if (membershipFlags != null && index < membershipFlags.Length)
                            {
                                membershipFlags[index] = true;
                            }
                        }
                    }
                }

                currentIndex = candidateIndex;
                if (guardMax < ++guard)
                {
                    break;
                }
            } while (currentIndex != startIndex);

            if (!includeColinearPoints && 2 < hull.Count)
            {
                PruneColinearOnHull(hull);
            }

            return hull;
        }

        private static int FindLowestWorldPositionIndex(Vector2[] worldPositions, int count)
        {
            if (worldPositions == null || count <= 0)
            {
                return -1;
            }

            int lowestIndex = -1;
            float lowestY = float.MaxValue;
            float lowestX = float.MaxValue;
            for (int i = 0; i < count; ++i)
            {
                Vector2 candidate = worldPositions[i];
                if (
                    lowestIndex < 0
                    || candidate.y < lowestY
                    || (Mathf.Approximately(candidate.y, lowestY) && candidate.x < lowestX)
                )
                {
                    lowestIndex = i;
                    lowestY = candidate.y;
                    lowestX = candidate.x;
                }
            }

            return lowestIndex;
        }

        private static void ResetBooleanFlags(bool[] flags, int count)
        {
            if (flags == null || count <= 0)
            {
                return;
            }

            int length = Math.Min(flags.Length, count);
            Array.Clear(flags, 0, length);
        }

        private static void SortIndicesByDistance(
            List<Vector2> points,
            Vector2 origin,
            List<int> indices,
            float[] scratchDistances
        )
        {
            if (indices == null || scratchDistances == null)
            {
                return;
            }

            int count = indices.Count;
            if (count <= 1)
            {
                return;
            }

            for (int i = 0; i < count; ++i)
            {
                int pointIndex = indices[i];
                scratchDistances[i] = (points[pointIndex] - origin).sqrMagnitude;
            }

            SelectionSort(indices, scratchDistances, count);
        }

        private static void SortIndicesByDistance(
            Vector2[] worldPositions,
            Vector2 origin,
            List<int> indices,
            float[] scratchDistances
        )
        {
            if (worldPositions == null || indices == null || scratchDistances == null)
            {
                return;
            }

            int count = indices.Count;
            if (count <= 1)
            {
                return;
            }

            for (int i = 0; i < count; ++i)
            {
                int pointIndex = indices[i];
                scratchDistances[i] = (worldPositions[pointIndex] - origin).sqrMagnitude;
            }

            SelectionSort(indices, scratchDistances, count);
        }

        private static void InsertNeighborCandidate(
            List<int> neighborIndices,
            float[] neighborDistances,
            int candidateIndex,
            float candidateDistance,
            ref int storedCount,
            int maximumCount
        )
        {
            if (maximumCount <= 0)
            {
                return;
            }

            if (neighborIndices == null || neighborDistances == null)
            {
                return;
            }

            if (neighborDistances.Length < maximumCount)
            {
                throw new ArgumentException(
                    "Neighbor distance buffer must be at least as large as maximumCount.",
                    nameof(neighborDistances)
                );
            }

            if (storedCount < maximumCount)
            {
                neighborIndices.Add(candidateIndex);
                neighborDistances[storedCount] = candidateDistance;
                int insertPosition = storedCount;
                while (
                    0 < insertPosition && candidateDistance < neighborDistances[insertPosition - 1]
                )
                {
                    neighborDistances[insertPosition] = neighborDistances[insertPosition - 1];
                    neighborIndices[insertPosition] = neighborIndices[insertPosition - 1];
                    --insertPosition;
                }

                neighborDistances[insertPosition] = candidateDistance;
                neighborIndices[insertPosition] = candidateIndex;
                ++storedCount;
                return;
            }

            if (neighborDistances[storedCount - 1] <= candidateDistance)
            {
                return;
            }

            int replacePosition = storedCount - 1;
            while (
                0 < replacePosition && candidateDistance < neighborDistances[replacePosition - 1]
            )
            {
                neighborDistances[replacePosition] = neighborDistances[replacePosition - 1];
                neighborIndices[replacePosition] = neighborIndices[replacePosition - 1];
                --replacePosition;
            }

            neighborDistances[replacePosition] = candidateDistance;
            neighborIndices[replacePosition] = candidateIndex;
        }

        private static void PopulateVectorBuffers(
            IEnumerable<FastVector3Int> source,
            List<Vector2> vectorPoints,
            Dictionary<Vector2, FastVector3Int> mapping,
            out int fallbackZ
        )
        {
            if (vectorPoints == null)
            {
                throw new ArgumentNullException(nameof(vectorPoints));
            }

            if (mapping == null)
            {
                throw new ArgumentNullException(nameof(mapping));
            }

            vectorPoints.Clear();
            mapping.Clear();
            int firstZ = 0;
            bool fallbackAssigned = false;

            if (source is ICollection<FastVector3Int> collection)
            {
                if (vectorPoints.Capacity < collection.Count)
                {
                    vectorPoints.Capacity = collection.Count;
                }
            }

            foreach (FastVector3Int point in source)
            {
                Vector2 vectorPoint = new(point.x, point.y);
                vectorPoints.Add(vectorPoint);
                mapping.TryAdd(vectorPoint, point);

                if (fallbackAssigned)
                {
                    continue;
                }

                firstZ = point.z;
                fallbackAssigned = true;
            }

            fallbackZ = firstZ;
        }

        private static List<FastVector3Int> ConvertVector2HullToFastVector3(
            IList<Vector2> vectorHull,
            Dictionary<Vector2, FastVector3Int> mapping,
            int fallbackZ
        )
        {
            if (vectorHull == null)
            {
                return new List<FastVector3Int>();
            }

            List<FastVector3Int> converted = new(vectorHull.Count);
            for (int i = 0; i < vectorHull.Count; ++i)
            {
                Vector2 vertex = vectorHull[i];
                if (!mapping.TryGetValue(vertex, out FastVector3Int point))
                {
                    Vector2 rounded = new(Mathf.Round(vertex.x), Mathf.Round(vertex.y));
                    if (!mapping.TryGetValue(rounded, out point))
                    {
                        point = new FastVector3Int(
                            Mathf.RoundToInt(vertex.x),
                            Mathf.RoundToInt(vertex.y),
                            fallbackZ
                        );
                    }
                }

                converted.Add(point);
            }

            return converted;
        }
    }
}
