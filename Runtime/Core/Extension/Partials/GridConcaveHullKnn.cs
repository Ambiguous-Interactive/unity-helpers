// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using DataStructure.Adapters;
    using UnityEngine;
    using Utils;

    // GridConcaveHullKnn.cs - K-Nearest Neighbors algorithm implementation
    // See GeometryConcaveHull.cs for full concave hull architecture documentation
    /// <summary>
    /// KNN-based concave hull builders for Vector2 and FastVector3Int grids.
    /// Iteratively selects next hull point from k nearest neighbors using maximum right turn.
    /// </summary>
    public static partial class UnityExtensions
    {
        public static List<Vector2> BuildConcaveHullKnn(
            this IReadOnlyCollection<Vector2> points,
            int nearestNeighbors = 3
        )
        {
            ConcaveHullOptions options = ConcaveHullOptions
                .Default.WithStrategy(ConcaveHullStrategy.Knn)
                .WithNearestNeighbors(Math.Max(3, nearestNeighbors));
            return points.BuildConcaveHull(options);
        }

        // KNN-style concave hull for Vector2 (port of BuildConcaveHull2)
        private static List<Vector2> BuildConcaveHull2(
            this IReadOnlyCollection<Vector2> input,
            int nearestNeighbors
        )
        {
            const int minimumNearestNeighbors = 3;
            nearestNeighbors = Math.Max(minimumNearestNeighbors, nearestNeighbors);

            using PooledResource<List<Vector2>> dataSetRes = Buffers<Vector2>.List.Get(
                out List<Vector2> dataSet
            );
            using PooledResource<HashSet<Vector2>> uniqueRes = Buffers<Vector2>.HashSet.Get(
                out HashSet<Vector2> unique
            );
            using PooledResource<List<Vector2>> originalRes = Buffers<Vector2>.List.Get(
                out List<Vector2> original
            );
            original.AddRange(input);

            foreach (Vector2 point in original)
            {
                if (unique.Add(point))
                {
                    dataSet.Add(point);
                }
            }

            int totalPoints = dataSet.Count;
            if (totalPoints <= 4)
            {
                return input.BuildConvexHull(includeColinearPoints: false);
            }

            int maximumNearestNeighbors = totalPoints;
            int attemptNearestNeighbors = Math.Min(totalPoints, nearestNeighbors);

            int firstIndex = FindLowestPointIndex(dataSet);
            List<Vector2> hull = new(totalPoints);
            if (firstIndex < 0)
            {
                hull.AddRange(dataSet);
                return hull;
            }

            Vector2 firstPoint = dataSet[firstIndex];
            int maxSteps = Math.Max(16, totalPoints * 6);

            using PooledArray<bool> availabilityResource = SystemArrayPool<bool>.Get(
                totalPoints,
                out bool[] availability
            );

            using PooledResource<List<int>> neighborIndicesRes = Buffers<int>.List.Get(
                out List<int> neighborIndices
            );

            using PooledArray<float> distanceBufferRes = SystemArrayPool<float>.Get(
                totalPoints,
                out float[] neighborDistances
            );

            while (true)
            {
                hull.Clear();
                hull.Add(firstPoint);

                if (neighborIndices.Capacity < attemptNearestNeighbors)
                {
                    neighborIndices.Capacity = attemptNearestNeighbors;
                }

                bool success = TryBuildConcaveHull2Attempt(
                    dataSet,
                    hull,
                    availability,
                    neighborIndices,
                    neighborDistances,
                    attemptNearestNeighbors,
                    firstIndex,
                    firstPoint,
                    maxSteps
                );

                if (success)
                {
                    PruneColinearOnHull(hull);
                    return hull;
                }

                if (maximumNearestNeighbors <= attemptNearestNeighbors)
                {
                    return BuildConvexHullJarvisFallback(
                        dataSet,
                        hull,
                        includeColinearPoints: false,
                        neighborIndices,
                        neighborDistances,
                        availability
                    );
                }

                ++attemptNearestNeighbors;
            }
        }

        /// <summary>
        /// Builds a concave hull using a k-nearest neighbors approach for grid points.
        /// </summary>
        /// <remarks>
        /// Obsolete: prefer <see cref="BuildConcaveHullKnn(IReadOnlyCollection{Vector2},int)"/> or
        /// <see cref="BuildConcaveHull(IReadOnlyCollection{Vector2}, System.Nullable{ConcaveHullOptions})"/>.
        /// </remarks>
        [Obsolete("Use BuildConcaveHullKnn or BuildConcaveHull with options.")]
        public static List<FastVector3Int> BuildConcaveHull2(
            this IReadOnlyCollection<FastVector3Int> gridPositions,
            Grid grid,
            int nearestNeighbors = 3
        )
        {
            const int minimumNearestNeighbors = 3;
            nearestNeighbors = Math.Max(minimumNearestNeighbors, nearestNeighbors);
            if (grid == null)
            {
                throw new ArgumentNullException(nameof(grid));
            }
            using PooledResource<List<FastVector3Int>> dataSetResource =
                Buffers<FastVector3Int>.List.Get(out List<FastVector3Int> dataSet);
            using PooledResource<HashSet<FastVector3Int>> uniquePositionsResource =
                Buffers<FastVector3Int>.HashSet.Get(out HashSet<FastVector3Int> uniquePositions);

            using PooledResource<List<FastVector3Int>> originalGridPositionsBuffer =
                Buffers<FastVector3Int>.List.Get(out List<FastVector3Int> originalGridPositions);
            originalGridPositions.AddRange(gridPositions);

            foreach (FastVector3Int gridPosition in originalGridPositions)
            {
                if (uniquePositions.Add(gridPosition))
                {
                    dataSet.Add(gridPosition);
                }
            }
            int totalPoints = dataSet.Count;
            List<FastVector3Int> hull = new(totalPoints);
            if (totalPoints <= 4)
            {
                return BuildConvexHullMonotoneChain(
                    gridPositions,
                    grid,
                    includeColinearPoints: false,
                    resultBuffer: hull
                );
            }

            BuildConvexHullMonotoneChain(
                dataSet,
                grid,
                includeColinearPoints: false,
                resultBuffer: hull
            );
            if (AreAllPointsOnHullEdges(dataSet, hull))
            {
                return hull;
            }
            hull.Clear();

            int maximumNearestNeighbors = totalPoints;
            int attemptNearestNeighbors = Math.Min(totalPoints, nearestNeighbors);

            int firstIndex = FindLowestGridPointIndex(dataSet, grid);
            if (firstIndex < 0)
            {
                hull.AddRange(dataSet);
                return hull;
            }

            FastVector3Int firstPoint = dataSet[firstIndex];
            int maxSteps = Math.Max(16, dataSet.Count * 6);

            using PooledArray<bool> availabilityResource = SystemArrayPool<bool>.Get(
                totalPoints,
                out bool[] availability
            );

            using PooledResource<List<int>> neighborIndicesResource = Buffers<int>.List.Get(
                out List<int> neighborIndices
            );

            using PooledArray<float> distanceBufferResource = SystemArrayPool<float>.Get(
                totalPoints,
                out float[] neighborDistances
            );

            using PooledArray<Vector2> worldPositionsResource = SystemArrayPool<Vector2>.Get(
                totalPoints,
                out Vector2[] worldPositions
            );
            for (int i = 0; i < totalPoints; ++i)
            {
                worldPositions[i] = grid.CellToWorld(dataSet[i]);
            }

            while (true)
            {
                hull.Clear();
                hull.Add(firstPoint);

                if (neighborIndices.Capacity < attemptNearestNeighbors)
                {
                    neighborIndices.Capacity = attemptNearestNeighbors;
                }

                bool success = TryBuildGridConcaveHullAttempt(
                    dataSet,
                    hull,
                    grid,
                    availability,
                    neighborIndices,
                    neighborDistances,
                    worldPositions,
                    attemptNearestNeighbors,
                    firstIndex,
                    firstPoint,
                    maxSteps
                );

                if (success)
                {
                    PruneColinearOnHull(hull);
                    return hull;
                }

                if (maximumNearestNeighbors <= attemptNearestNeighbors)
                {
                    return BuildGridConvexHullJarvisFallback(
                        dataSet,
                        worldPositions,
                        hull,
                        includeColinearPoints: false,
                        neighborIndices,
                        neighborDistances,
                        availability
                    );
                }

                ++attemptNearestNeighbors;
            }
        }

        private static void SortByDistanceAscending(List<Vector2> points, Vector2 origin)
        {
            int count = points.Count;
            if (count <= 1)
            {
                return;
            }
            using PooledArray<float> distancesResource = SystemArrayPool<float>.Get(
                count,
                out float[] distances
            );
            for (int i = 0; i < count; ++i)
            {
                distances[i] = (points[i] - origin).sqrMagnitude;
            }
            SelectionSort(points, distances, count);
        }

        private static void SortByRightHandTurn(
            List<Vector2> points,
            Vector2 current,
            float previousAngle
        )
        {
            int count = points.Count;
            if (count <= 1)
            {
                return;
            }
            using PooledArray<float> angleRes = SystemArrayPool<float>.Get(
                count,
                out float[] angles
            );
            for (int i = 0; i < count; ++i)
            {
                float candidateAngle = CalculateAngle(current, points[i]);
                angles[i] = -AngleDifference(previousAngle, candidateAngle);
            }
            SelectionSort(points, angles, count);
        }

        private static void SortByDistanceAscending(
            List<Vector3Int> points,
            Grid grid,
            Vector2 origin
        )
        {
            int count = points.Count;
            if (count <= 1)
            {
                return;
            }

            using PooledArray<float> distancesResource = SystemArrayPool<float>.Get(
                count,
                out float[] distances
            );
            for (int i = 0; i < count; ++i)
            {
                Vector2 worldPosition = grid.CellToWorld(points[i]);
                distances[i] = (worldPosition - origin).sqrMagnitude;
            }

            SelectionSort(points, distances, count);
        }

        private static void SortByDistanceAscending(
            List<FastVector3Int> points,
            Grid grid,
            Vector2 origin
        )
        {
            int count = points.Count;
            if (count <= 1)
            {
                return;
            }

            using PooledArray<float> distancesResource = SystemArrayPool<float>.Get(
                count,
                out float[] distances
            );
            for (int i = 0; i < count; ++i)
            {
                Vector2 worldPosition = grid.CellToWorld(points[i]);
                distances[i] = (worldPosition - origin).sqrMagnitude;
            }

            SelectionSort(points, distances, count);
        }

        private static void SortByRightHandTurn(
            List<FastVector3Int> points,
            Grid grid,
            FastVector3Int current,
            float previousAngle
        )
        {
            int count = points.Count;
            if (count <= 1)
            {
                return;
            }

            using PooledArray<float> angleBufferResource = SystemArrayPool<float>.Get(
                count,
                out float[] angles
            );
            Vector2 currentPoint = grid.CellToWorld(current);
            for (int i = 0; i < count; ++i)
            {
                Vector2 candidatePoint = grid.CellToWorld(points[i]);
                float candidateAngle = CalculateAngle(currentPoint, candidatePoint);
                angles[i] = -AngleDifference(previousAngle, candidateAngle);
            }

            SelectionSort(points, angles, count);
        }

        private static void SelectionSort<T>(List<T> points, float[] distances, int count)
        {
            for (int i = 0; i < count - 1; ++i)
            {
                int minIndex = i;
                float minDistance = distances[i];
                for (int j = i + 1; j < count; ++j)
                {
                    float candidateDistance = distances[j];
                    if (candidateDistance < minDistance)
                    {
                        minDistance = candidateDistance;
                        minIndex = j;
                    }
                }

                if (minIndex != i)
                {
                    (points[i], points[minIndex]) = (points[minIndex], points[i]);
                    (distances[i], distances[minIndex]) = (distances[minIndex], distances[i]);
                }
            }
        }

        private static void RemovePoints<T>(List<T> source, List<T> toRemove)
        {
            for (int i = toRemove.Count - 1; 0 <= i; --i)
            {
                _ = source.Remove(toRemove[i]);
            }
        }
    }
}
