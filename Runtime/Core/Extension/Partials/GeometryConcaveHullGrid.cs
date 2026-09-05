// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using DataStructure.Adapters;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Grid-aware concave hull entry points (FastVector3Int + Grid contexts).
    /// Provides hull generation for Unity Grid-based tile systems.
    /// </summary>
    public static partial class UnityExtensions
    {
        /// <summary>
        /// Unified concave hull entry point with explicit strategy handling for Grid contexts.
        /// </summary>
        public static List<FastVector3Int> BuildConcaveHull(
            this IReadOnlyCollection<FastVector3Int> gridPositions,
            Grid grid,
            ConcaveHullOptions? options = null
        )
        {
            ConcaveHullOptions appliedOptions = options ?? ConcaveHullOptions.Default;
            using PooledResource<List<FastVector3Int>> sourcePointsLease =
                Buffers<FastVector3Int>.List.Get(out List<FastVector3Int> sourcePoints);
            sourcePoints.AddRange(
                gridPositions ?? throw new ArgumentNullException(nameof(gridPositions))
            );
            List<FastVector3Int> hull;
            switch (appliedOptions.Strategy)
            {
                case ConcaveHullStrategy.Knn:
#pragma warning disable CS0618
                    hull = gridPositions.BuildConcaveHull2(
                        grid,
                        Math.Max(3, appliedOptions.NearestNeighbors)
                    );
#pragma warning restore CS0618
                    break;
                case ConcaveHullStrategy.EdgeSplit:
#pragma warning disable CS0618
                    hull = gridPositions.BuildConcaveHull3(
                        grid,
                        Math.Max(1, appliedOptions.BucketSize),
                        appliedOptions.AngleThreshold
                    );
#pragma warning restore CS0618
                    break;
                default:
                    throw new InvalidEnumArgumentException(
                        nameof(appliedOptions.Strategy),
                        (int)appliedOptions.Strategy,
                        typeof(ConcaveHullStrategy)
                    );
            }

#if ENABLE_CONCAVE_HULL_STATS
            ConcaveHullRepairStats repairStats = new(hull.Count, sourcePoints.Count);
            MaybeRepairConcaveCorners(
                hull,
                sourcePoints,
                appliedOptions.Strategy,
                appliedOptions.AngleThreshold,
                repairStats
            );
            TrackHullRepairStats(hull, repairStats);
#else
            MaybeRepairConcaveCorners(
                hull,
                sourcePoints,
                appliedOptions.Strategy,
                appliedOptions.AngleThreshold
            );
#endif
            return hull;
        }

        public static List<FastVector3Int> BuildConcaveHullKnn(
            this IReadOnlyCollection<FastVector3Int> gridPositions,
            Grid grid,
            int nearestNeighbors = 3
        )
        {
#pragma warning disable CS0618
            ConcaveHullOptions options = ConcaveHullOptions
                .Default.WithStrategy(ConcaveHullStrategy.Knn)
                .WithNearestNeighbors(Math.Max(3, nearestNeighbors));
            return gridPositions.BuildConcaveHull(grid, options);
#pragma warning restore CS0618
        }

        public static List<FastVector3Int> BuildConcaveHullEdgeSplit(
            this IReadOnlyCollection<FastVector3Int> gridPositions,
            Grid grid,
            int bucketSize = 40,
            float angleThreshold = 90f
        )
        {
#pragma warning disable CS0618
            int clampedBucketSize = Math.Max(1, bucketSize);
            float effectiveAngleThreshold = clampedBucketSize <= 1 ? 0f : angleThreshold;
            ConcaveHullOptions options = ConcaveHullOptions
                .Default.WithStrategy(ConcaveHullStrategy.EdgeSplit)
                .WithBucketSize(clampedBucketSize)
                .WithAngleThreshold(effectiveAngleThreshold);
            return gridPositions.BuildConcaveHull(grid, options);
#pragma warning restore CS0618
        }
    }
}
