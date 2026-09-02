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
    using Utils;

    // GridConcaveHull.cs - Strategy dispatcher and Vector2 entry point
    // See GeometryConcaveHull.cs for full concave hull architecture documentation
    /// <summary>
    /// Strategy dispatcher for concave hull builders. Routes to KNN or EdgeSplit algorithm based on options.
    /// </summary>
    public static partial class UnityExtensions
    {
        private const float Vector2AxisSnapTolerance = 1e-3f;

        public static List<Vector2> BuildConcaveHull(
            this IReadOnlyCollection<Vector2> points,
            ConcaveHullOptions? options = null
        )
        {
            ConcaveHullOptions appliedOptions = options ?? ConcaveHullOptions.Default;
            List<Vector2> hull = BuildConcaveHullRaw(points, appliedOptions);

            if (
                appliedOptions.Strategy == ConcaveHullStrategy.Knn
                || ShouldRepairConcaveCorners(appliedOptions.AngleThreshold)
            )
            {
                hull = RepairVector2Hull(
                    points,
                    hull,
                    appliedOptions.Strategy,
                    appliedOptions.AngleThreshold
                );
            }

            return hull;
        }

        private static List<Vector2> BuildConcaveHullRaw(
            IReadOnlyCollection<Vector2> points,
            ConcaveHullOptions options
        )
        {
            switch (options.Strategy)
            {
                case ConcaveHullStrategy.Knn:
                    return points.BuildConcaveHull2(Math.Max(3, options.NearestNeighbors));
                case ConcaveHullStrategy.EdgeSplit:
                    return points.BuildConcaveHull3(
                        Math.Max(1, options.BucketSize),
                        options.AngleThreshold
                    );
                default:
                    throw new InvalidEnumArgumentException(
                        nameof(options.Strategy),
                        (int)options.Strategy,
                        typeof(ConcaveHullStrategy)
                    );
            }
        }

        private static List<Vector2> RepairVector2Hull(
            IReadOnlyCollection<Vector2> originalPoints,
            List<Vector2> hull,
            ConcaveHullStrategy strategy,
            float angleThreshold
        )
        {
            if (hull == null)
            {
                return new List<Vector2>();
            }

            if (!AreVector2PointsAxisAligned(originalPoints) || !AreVector2PointsAxisAligned(hull))
            {
                /*
                    Axis-corner repair only applies to lattice-aligned datasets; skip to avoid
                    rounding when working with arbitrary floating-point inputs.
                */
                return hull;
            }

            using PooledResource<List<FastVector3Int>> fastHullResource =
                Buffers<FastVector3Int>.List.Get(out List<FastVector3Int> fastHull);
            using PooledResource<List<FastVector3Int>> fastOriginalResource =
                Buffers<FastVector3Int>.List.Get(out List<FastVector3Int> fastOriginal);

            ConvertVector2CollectionToFastVector3(hull, fastHull);
            ConvertVector2CollectionToFastVector3(originalPoints, fastOriginal);

#if ENABLE_CONCAVE_HULL_STATS
            ConcaveHullRepairStats repairStats = new(fastHull.Count, fastOriginal.Count);
            MaybeRepairConcaveCorners(
                fastHull,
                fastOriginal,
                strategy,
                angleThreshold,
                repairStats
            );
#else
            MaybeRepairConcaveCorners(fastHull, fastOriginal, strategy, angleThreshold);
#endif

            hull.Clear();
            foreach (FastVector3Int vertex in fastHull)
            {
                hull.Add(new Vector2(vertex.x, vertex.y));
            }

            return hull;
        }

        private static void ConvertVector2CollectionToFastVector3(
            IEnumerable<Vector2> source,
            List<FastVector3Int> destination
        )
        {
            destination.Clear();
            foreach (Vector2 point in source)
            {
                destination.Add(
                    new FastVector3Int(Mathf.RoundToInt(point.x), Mathf.RoundToInt(point.y), 0)
                );
            }
        }

        private static bool AreVector2PointsAxisAligned(IEnumerable<Vector2> source)
        {
            foreach (Vector2 point in source)
            {
                if (
                    !IsApproximatelyInteger(point.x, Vector2AxisSnapTolerance)
                    || !IsApproximatelyInteger(point.y, Vector2AxisSnapTolerance)
                )
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsApproximatelyInteger(float value, float tolerance)
        {
            return Mathf.Abs(value - Mathf.Round(value)) <= tolerance;
        }
    }
}
