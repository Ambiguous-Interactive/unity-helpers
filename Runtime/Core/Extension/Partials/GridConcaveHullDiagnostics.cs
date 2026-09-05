// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS || WALLSTOP_CONCAVE_HULL_STATS
#define ENABLE_CONCAVE_HULL_STATS
#endif

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using DataStructure.Adapters;
    using UnityEngine;
    using Utils;

    /// <summary>
    /// Concave hull diagnostics, repair helpers, and shared geometry utilities.
    /// Contains repair stats tracking and axis-corner repair logic for grid-aligned data.
    /// </summary>
    public static partial class UnityExtensions
    {
#if ENABLE_CONCAVE_HULL_STATS
        private static readonly ConditionalWeakTable<
            List<FastVector3Int>,
            ConcaveHullRepairStats
        > HullRepairStatsCache = new();

        private static void TrackHullRepairStats(
            List<FastVector3Int> hull,
            ConcaveHullRepairStats stats
        )
        {
            if (hull == null || stats == null)
            {
                return;
            }

            ConcaveHullRepairStats clone = stats.Clone();
            if (HullRepairStatsCache.TryGetValue(hull, out _))
            {
                HullRepairStatsCache.Remove(hull);
            }

            HullRepairStatsCache.Add(hull, clone);
        }

        private static bool TryGetTrackedHullRepairStats(
            List<FastVector3Int> hull,
            out ConcaveHullRepairStats stats
        )
        {
            if (
                hull != null
                && HullRepairStatsCache.TryGetValue(hull, out ConcaveHullRepairStats cached)
            )
            {
                stats = cached.Clone();
                return true;
            }

            stats = null;
            return false;
        }
#endif

        private const float ConcaveCornerRepairThresholdDegrees = 90f;
        private const int AxisCornerRepairIterationMultiplier = 4;
        private const int AxisCornerRepairIterationMinimum = 1024;

        /// <summary>
        /// Calculates the cosine of the angle at point o formed by points a and b using the law of cosines.
        /// </summary>
        public static double GetCosine(Vector2 a, Vector3 b, Vector3 o)
        {
            double aPow2 = (a.x - o.x) * (a.x - o.x) + (a.y - o.y) * (a.y - o.y);
            double bPow2 = (b.x - o.x) * (b.x - o.x) + (b.y - o.y) * (b.y - o.y);
            double cPow2 = (a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y);
            double cos = (aPow2 + bPow2 - cPow2) / (2 * Math.Sqrt(aPow2 * bPow2));
            return Math.Round(cos, 4);
        }

        public static bool IsPositionInside(List<Vector2> hull, Vector2 position)
        {
            bool isPositionInside = false;
            for (int i = 0; i < hull.Count; ++i)
            {
                Vector2 oldVector = hull[i];
                int nextIndex = (i + 1) % hull.Count;
                Vector2 newVector = hull[nextIndex];

                Vector2 lhs;
                Vector2 rhs;
                if (oldVector.x < newVector.x)
                {
                    lhs = oldVector;
                    rhs = newVector;
                }
                else
                {
                    lhs = newVector;
                    rhs = oldVector;
                }

                if (
                    (newVector.x < position.x) == (position.x <= oldVector.x)
                    && (position.y - (long)lhs.y) * (rhs.x - lhs.x)
                        < (rhs.y - (long)lhs.y) * (position.x - lhs.x)
                )
                {
                    isPositionInside = !isPositionInside;
                }
            }
            return isPositionInside;
        }

        public static bool IsPositionInside(
            List<FastVector3Int> hull,
            FastVector3Int gridPosition,
            Grid grid
        )
        {
            bool isPositionInside = false;
            Vector2 position = grid.CellToWorld(gridPosition);
            for (int i = 0; i < hull.Count; ++i)
            {
                Vector2 oldVector = grid.CellToWorld(hull[i]);
                int nextIndex = (i + 1) % hull.Count;
                Vector2 newVector = grid.CellToWorld(hull[nextIndex]);

                Vector2 lhs;
                Vector2 rhs;
                if (oldVector.x < newVector.x)
                {
                    lhs = oldVector;
                    rhs = newVector;
                }
                else
                {
                    lhs = newVector;
                    rhs = oldVector;
                }

                if (
                    newVector.x < position.x == position.x <= oldVector.x
                    && (position.y - (long)lhs.y) * (rhs.x - lhs.x)
                        < (rhs.y - (long)lhs.y) * (position.x - lhs.x)
                )
                {
                    isPositionInside = !isPositionInside;
                }
            }

            return isPositionInside;
        }

        private static bool ShouldRepairConcaveCorners(float angleThreshold)
        {
            return ConcaveCornerRepairThresholdDegrees <= angleThreshold;
        }

        private static void MaybeRepairConcaveCorners(
            List<FastVector3Int> hull,
            List<FastVector3Int> originalPoints,
            ConcaveHullStrategy strategy,
            float angleThreshold
#if ENABLE_CONCAVE_HULL_STATS
            ,
            ConcaveHullRepairStats stats = null
#endif
        )
        {
            if (
                hull == null
                || originalPoints == null
                || (
                    strategy != ConcaveHullStrategy.Knn
                    && !ShouldRepairConcaveCorners(angleThreshold)
                )
            )
            {
                return;
            }

#if ENABLE_CONCAVE_HULL_STATS
            InsertMissingAxisCorners(hull, originalPoints, stats);
            RemoveDuplicateVertices(hull, stats);
#else
            InsertMissingAxisCorners(hull, originalPoints);
            RemoveDuplicateVertices(hull);
#endif
        }

        private static void InsertMissingAxisCorners(
            List<FastVector3Int> hull,
            List<FastVector3Int> originalPoints
#if ENABLE_CONCAVE_HULL_STATS
            ,
            ConcaveHullRepairStats stats
#endif
        )
        {
            if (hull == null || originalPoints == null || hull.Count < 3)
            {
                return;
            }

            using PooledResource<HashSet<FastVector3Int>> pointSetResource =
                Buffers<FastVector3Int>.HashSet.Get(out HashSet<FastVector3Int> pointSet);
            pointSet.UnionWith(originalPoints);

            using PooledResource<HashSet<FastVector3Int>> hullSetResource =
                Buffers<FastVector3Int>.HashSet.Get(out HashSet<FastVector3Int> hullSet);
            hullSet.UnionWith(hull);
            using PooledResource<List<FastVector3Int>> axisPathLease =
                Buffers<FastVector3Int>.List.Get(out List<FastVector3Int> axisPathBuffer);

            int uniquePointBudget = Math.Max(1, pointSet.Count);
            int iterationGuardLimit = Math.Max(
                AxisCornerRepairIterationMinimum,
                uniquePointBudget * AxisCornerRepairIterationMultiplier
            );
            int iterationCount = 0;
            bool inserted;
            do
            {
                if (iterationGuardLimit <= iterationCount++)
                {
#if ENABLE_CONCAVE_HULL_STATS
                    throw new InvalidOperationException(
                        $"InsertMissingAxisCorners exceeded iteration guard limit ({iterationGuardLimit}). Stats => AxisCorners:{stats?.AxisCornerInsertions ?? 0}, AxisPaths:{stats?.AxisPathInsertions ?? 0}, Candidates:{stats?.CandidateConnections ?? 0}."
                    );
#else
                    throw new InvalidOperationException(
                        $"InsertMissingAxisCorners exceeded iteration guard limit ({iterationGuardLimit}). Enable ENABLE_CONCAVE_HULL_STATS for more diagnostics."
                    );
#endif
                }

                inserted = false;
                int count = hull.Count;
                for (int i = 0; i < count; ++i)
                {
                    int nextIndex = (i + 1) % count;
                    FastVector3Int start = hull[i];
                    FastVector3Int end = hull[nextIndex];
                    if (start.x == end.x || start.y == end.y)
                    {
                        continue;
                    }

                    _ = pointSet.Add(start);
                    _ = pointSet.Add(end);

#if ENABLE_CONCAVE_HULL_STATS
                    axisPathBuffer.Clear();
                    bool foundPath = TryFindAxisPath(
                        start,
                        end,
                        pointSet,
                        allowStraightAxisFallback: false,
                        axisPathBuffer,
                        stats
                    );
#else
                    axisPathBuffer.Clear();
                    bool foundPath = TryFindAxisPath(
                        start,
                        end,
                        pointSet,
                        allowStraightAxisFallback: false,
                        axisPathBuffer
                    );
#endif
                    if (foundPath && 2 < axisPathBuffer.Count)
                    {
                        int insertIndex = nextIndex;
                        bool insertedWaypoint = false;
                        for (int p = 1; p < axisPathBuffer.Count - 1; ++p)
                        {
                            FastVector3Int waypoint = axisPathBuffer[p];
                            if (!hullSet.Add(waypoint))
                            {
                                continue;
                            }

                            InsertCorner(hull, insertIndex, waypoint);
                            ++insertIndex;
                            insertedWaypoint = true;
                            pointSet.Add(waypoint);
#if ENABLE_CONCAVE_HULL_STATS
                            stats?.IncrementAxisPathInsertions(1);
#endif
                        }

                        if (!insertedWaypoint)
                        {
                            continue;
                        }

                        inserted = true;
                        break;
                    }

                    FastVector3Int candidateA = new(start.x, end.y, start.z);
                    FastVector3Int candidateB = new(end.x, start.y, start.z);
                    if (pointSet.Contains(candidateA) && hullSet.Add(candidateA))
                    {
                        InsertCorner(hull, nextIndex, candidateA);
                        pointSet.Add(candidateA);
#if ENABLE_CONCAVE_HULL_STATS
                        stats?.IncrementAxisCornerInsertions();
#endif
                        inserted = true;
                        break;
                    }

                    if (pointSet.Contains(candidateB) && hullSet.Add(candidateB))
                    {
                        InsertCorner(hull, nextIndex, candidateB);
                        pointSet.Add(candidateB);
#if ENABLE_CONCAVE_HULL_STATS
                        stats?.IncrementAxisCornerInsertions();
#endif
                        inserted = true;
                        break;
                    }
                }
            } while (inserted);

#if ENABLE_CONCAVE_HULL_STATS
            EnsureAxisCornersIncluded(hull, originalPoints, pointSet, stats);
            PruneDiagonalOnlyVertices(hull, pointSet, stats);
#else
            EnsureAxisCornersIncluded(hull, originalPoints, pointSet);
            PruneDiagonalOnlyVertices(hull, pointSet);
#endif
        }

        private static bool TryFindAxisPath(
            FastVector3Int start,
            FastVector3Int end,
            HashSet<FastVector3Int> pointSet,
            bool allowStraightAxisFallback,
            List<FastVector3Int> result
#if ENABLE_CONCAVE_HULL_STATS
            ,
            ConcaveHullRepairStats stats
#endif
        )
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            result.Clear();

            using PooledResource<Queue<FastVector3Int>> queueResource =
                Buffers<FastVector3Int>.Queue.Get(out Queue<FastVector3Int> frontier);
            using PooledResource<Dictionary<FastVector3Int, FastVector3Int>> parentResource =
                DictionaryBuffer<FastVector3Int, FastVector3Int>.Dictionary.Get(
                    out Dictionary<FastVector3Int, FastVector3Int> parents
                );

            frontier.Enqueue(start);
            parents[start] = start;

            while (0 < frontier.Count)
            {
#if ENABLE_CONCAVE_HULL_STATS
                stats?.MaybeRecordFrontierSize(frontier.Count);
#endif
                FastVector3Int current = frontier.Dequeue();
                if (current == end)
                {
                    break;
                }

                foreach (FastVector3Int neighbor in EnumerateAxisNeighbors(current, pointSet))
                {
#if ENABLE_CONCAVE_HULL_STATS
                    stats?.IncrementAxisNeighborVisits();
#endif
                    if (parents.ContainsKey(neighbor))
                    {
                        continue;
                    }

                    parents[neighbor] = current;
                    frontier.Enqueue(neighbor);
                }
            }

            if (!parents.ContainsKey(end))
            {
                if (allowStraightAxisFallback && (start.x == end.x || start.y == end.y))
                {
                    result.Add(start);
                    result.Add(end);
                    return true;
                }

                return false;
            }

            FastVector3Int cursor = end;
            result.Add(cursor);
            while (cursor != start)
            {
                if (!parents.TryGetValue(cursor, out FastVector3Int parent))
                {
                    result.Clear();
                    return false;
                }

                cursor = parent;
                result.Add(cursor);
            }

            result.Reverse();
            return true;
        }

        private static IEnumerable<FastVector3Int> EnumerateAxisNeighbors(
            FastVector3Int origin,
            HashSet<FastVector3Int> pointSet
        )
        {
            FastVector3Int right = new(origin.x + 1, origin.y, origin.z);
            if (pointSet.Contains(right))
            {
                yield return right;
            }

            FastVector3Int left = new(origin.x - 1, origin.y, origin.z);
            if (pointSet.Contains(left))
            {
                yield return left;
            }

            FastVector3Int up = new(origin.x, origin.y + 1, origin.z);
            if (pointSet.Contains(up))
            {
                yield return up;
            }

            FastVector3Int down = new(origin.x, origin.y - 1, origin.z);
            if (pointSet.Contains(down))
            {
                yield return down;
            }
        }

        private static void InsertCorner(
            List<FastVector3Int> hull,
            int insertIndex,
            FastVector3Int candidate
        )
        {
            if (insertIndex == hull.Count)
            {
                hull.Add(candidate);
            }
            else
            {
                hull.Insert(insertIndex, candidate);
            }
        }

        private static void EnsureAxisCornersIncluded(
            List<FastVector3Int> hull,
            List<FastVector3Int> originalPoints,
            HashSet<FastVector3Int> pointSet
#if ENABLE_CONCAVE_HULL_STATS
            ,
            ConcaveHullRepairStats stats
#endif
        )
        {
            if (hull == null || originalPoints == null)
            {
                return;
            }

            using PooledResource<HashSet<FastVector3Int>> hullSetResource =
                Buffers<FastVector3Int>.HashSet.Get(out HashSet<FastVector3Int> hullSet);
            hullSet.UnionWith(hull);

            bool added;
            do
            {
                added = false;
                foreach (FastVector3Int candidate in originalPoints)
                {
                    if (hullSet.Contains(candidate))
                    {
                        continue;
                    }

                    if (!HasAxisCornerSupport(candidate, pointSet))
                    {
                        continue;
                    }

#if ENABLE_CONCAVE_HULL_STATS
                    if (!TryConnectCandidateToHull(candidate, hull, pointSet, hullSet, stats))
#else
                    if (!TryConnectCandidateToHull(candidate, hull, pointSet, hullSet))
#endif
                    {
                        continue;
                    }

#if ENABLE_CONCAVE_HULL_STATS
                    stats?.IncrementCandidateConnections();
#endif
                    added = true;
                    break;
                }
            } while (added);
        }

        private static bool HasAxisCornerSupport(
            FastVector3Int candidate,
            HashSet<FastVector3Int> points
        )
        {
            if (points == null || points.Count == 0)
            {
                return false;
            }

            bool hasX = false;
            bool hasY = false;
            foreach (FastVector3Int point in points)
            {
                if (point == candidate)
                {
                    continue;
                }

                if (!hasX && point.x == candidate.x)
                {
                    hasX = true;
                }

                if (!hasY && point.y == candidate.y)
                {
                    hasY = true;
                }

                if (hasX && hasY)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryConnectCandidateToHull(
            FastVector3Int candidate,
            List<FastVector3Int> hull,
            HashSet<FastVector3Int> pointSet,
            HashSet<FastVector3Int> hullSet
#if ENABLE_CONCAVE_HULL_STATS
            ,
            ConcaveHullRepairStats stats
#endif
        )
        {
            if (hull == null || hull.Count < 2)
            {
                return false;
            }

            int count = hull.Count;
            using PooledResource<List<FastVector3Int>> axisPathLease =
                Buffers<FastVector3Int>.List.Get(out List<FastVector3Int> axisPathBuffer);
            for (int i = 0; i < count; ++i)
            {
                FastVector3Int anchor = hull[i];
                if (anchor.x != candidate.x && anchor.y != candidate.y)
                {
                    continue;
                }

#if ENABLE_CONCAVE_HULL_STATS
                axisPathBuffer.Clear();
                bool foundPath = TryFindAxisPath(
                    anchor,
                    candidate,
                    pointSet,
                    allowStraightAxisFallback: true,
                    axisPathBuffer,
                    stats
                );
#else
                axisPathBuffer.Clear();
                bool foundPath = TryFindAxisPath(
                    anchor,
                    candidate,
                    pointSet,
                    allowStraightAxisFallback: true,
                    axisPathBuffer
                );
#endif
                if (!foundPath || axisPathBuffer.Count <= 1)
                {
                    continue;
                }

#if ENABLE_CONCAVE_HULL_STATS
                bool inserted = InsertPathAfterIndex(
                    hull,
                    i,
                    axisPathBuffer,
                    hullSet,
                    pointSet,
                    stats
                );
#else
                bool inserted = InsertPathAfterIndex(hull, i, axisPathBuffer, hullSet, pointSet);
#endif
                if (inserted)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool InsertPathAfterIndex(
            List<FastVector3Int> hull,
            int anchorIndex,
            List<FastVector3Int> path,
            HashSet<FastVector3Int> hullSet,
            HashSet<FastVector3Int> pointSet
#if ENABLE_CONCAVE_HULL_STATS
            ,
            ConcaveHullRepairStats stats
#endif
        )
        {
            int insertIndex = anchorIndex + 1;
            bool inserted = false;
            for (int p = 1; p < path.Count; ++p)
            {
                FastVector3Int waypoint = path[p];
                if (hullSet != null && !hullSet.Add(waypoint))
                {
                    continue;
                }

                if (hull.Count <= insertIndex)
                {
                    hull.Add(waypoint);
                }
                else
                {
                    hull.Insert(insertIndex, waypoint);
                }

                pointSet?.Add(waypoint);
                inserted = true;
                ++insertIndex;
#if ENABLE_CONCAVE_HULL_STATS
                if (p < path.Count - 1)
                {
                    stats?.IncrementAxisPathInsertions(1);
                }
#endif
            }

            return inserted;
        }

        private static bool TryInsertAxisCorner(List<FastVector3Int> hull, FastVector3Int candidate)
        {
            if (hull == null || hull.Count < 2)
            {
                return false;
            }

            int count = hull.Count;
            for (int i = 0; i < count; ++i)
            {
                FastVector3Int prev = hull[i];
                FastVector3Int next = hull[(i + 1) % count];
                bool prevSharesX = prev.x == candidate.x && IsBetween(candidate.y, prev.y, next.y);
                bool prevSharesY = prev.y == candidate.y && IsBetween(candidate.x, prev.x, next.x);
                bool nextSharesX = next.x == candidate.x && IsBetween(candidate.y, prev.y, next.y);
                bool nextSharesY = next.y == candidate.y && IsBetween(candidate.x, prev.x, next.x);
                if ((prevSharesX && nextSharesY) || (prevSharesY && nextSharesX))
                {
                    InsertCorner(hull, (i + 1) % count, candidate);
                    return true;
                }
            }

            return false;
        }

        private static bool IsBetween(int value, int boundA, int boundB)
        {
            return boundA <= boundB
                ? boundA <= value && value <= boundB
                : boundB <= value && value <= boundA;
        }

        private static bool HasSharedAxisNeighbor(
            FastVector3Int candidate,
            HashSet<FastVector3Int> points
        )
        {
            if (points == null || points.Count == 0)
            {
                return false;
            }

            foreach (FastVector3Int point in points)
            {
                if (point == candidate)
                {
                    continue;
                }

                if (point.x == candidate.x || point.y == candidate.y)
                {
                    return true;
                }
            }

            return false;
        }

        private static void PruneDiagonalOnlyVertices(
            List<FastVector3Int> hull,
            HashSet<FastVector3Int> originalPoints
#if ENABLE_CONCAVE_HULL_STATS
            ,
            ConcaveHullRepairStats stats
#endif
        )
        {
            if (hull == null || originalPoints == null || originalPoints.Count == 0)
            {
                return;
            }

            int minX = int.MaxValue;
            int maxX = int.MinValue;
            int minY = int.MaxValue;
            int maxY = int.MinValue;
            foreach (FastVector3Int point in originalPoints)
            {
                if (point.x < minX)
                {
                    minX = point.x;
                }

                if (maxX < point.x)
                {
                    maxX = point.x;
                }

                if (point.y < minY)
                {
                    minY = point.y;
                }

                if (maxY < point.y)
                {
                    maxY = point.y;
                }
            }

            for (int i = hull.Count - 1; 0 <= i; --i)
            {
                FastVector3Int vertex = hull[i];
                if (HasSharedAxisNeighbor(vertex, originalPoints))
                {
                    continue;
                }

                bool insideX = minX < vertex.x && vertex.x < maxX;
                bool insideY = minY < vertex.y && vertex.y < maxY;
                if (insideX && insideY)
                {
                    hull.RemoveAt(i);
#if ENABLE_CONCAVE_HULL_STATS
                    stats?.IncrementDiagonalPruned();
#endif
                }
            }
        }

        private static float CalculateAngle(Vector2 lhs, Vector2 rhs)
        {
            return Mathf.Atan2(rhs.y - lhs.y, rhs.x - lhs.x);
        }

        private static float AngleDifference(float lhsAngle, float rhsAngle)
        {
            float delta = lhsAngle - rhsAngle;
            float twoPi = Mathf.PI * 2f;
            delta %= twoPi;
            if (delta < 0f)
            {
                delta += twoPi;
            }

            return delta;
        }

        public static bool Intersects(
            Vector2 lhsFrom,
            Vector2 lhsTo,
            Vector2 rhsFrom,
            Vector2 rhsTo
        )
        {
            if (lhsFrom == rhsFrom || lhsFrom == rhsTo || lhsTo == rhsFrom || lhsTo == rhsTo)
            {
                return false;
            }

            OrientationType orientation1 = Orientation(lhsFrom, lhsTo, rhsFrom);
            OrientationType orientation2 = Orientation(lhsFrom, lhsTo, rhsTo);
            OrientationType orientation3 = Orientation(rhsFrom, rhsTo, lhsFrom);
            OrientationType orientation4 = Orientation(rhsFrom, rhsTo, lhsTo);

            if (orientation1 != orientation2 && orientation3 != orientation4)
            {
                return true;
            }

            if (orientation1 == OrientationType.Colinear && LiesOnSegment(lhsFrom, rhsFrom, lhsTo))
            {
                return true;
            }

            if (orientation2 == OrientationType.Colinear && LiesOnSegment(lhsFrom, rhsTo, lhsTo))
            {
                return true;
            }

            if (orientation3 == OrientationType.Colinear && LiesOnSegment(rhsFrom, lhsFrom, rhsTo))
            {
                return true;
            }

            if (orientation4 == OrientationType.Colinear && LiesOnSegment(rhsFrom, lhsTo, rhsTo))
            {
                return true;
            }

            return false;
        }

        public static bool LiesOnSegment(Vector2 p, Vector2 q, Vector2 r)
        {
            return q.x <= Math.Max(p.x, r.x)
                && Math.Min(p.x, r.x) <= q.x
                && q.y <= Math.Max(p.y, r.y)
                && Math.Min(p.y, r.y) <= q.y;
        }

        public static OrientationType Orientation(Vector2 p, Vector2 q, Vector2 r)
        {
            float value = (q.y - p.y) * (r.x - q.x) - (q.x - p.x) * (r.y - q.y);
            if (Mathf.Approximately(value, 0))
            {
                return OrientationType.Colinear;
            }

            return 0 < value ? OrientationType.Clockwise : OrientationType.Counterclockwise;
        }

        public static Vector2 Rotate(this Vector2 v, float degrees)
        {
            float sin = Mathf.Sin(degrees * Mathf.Deg2Rad);
            float cos = Mathf.Cos(degrees * Mathf.Deg2Rad);

            float tx = v.x;
            float ty = v.y;

            Vector2 rotatedVector;
            rotatedVector.x = cos * tx - sin * ty;
            rotatedVector.y = sin * tx + cos * ty;

            return rotatedVector;
        }

#if ENABLE_CONCAVE_HULL_STATS
        public static ConcaveHullRepairStats ProfileConcaveHullRepair(
            List<FastVector3Int> hull,
            List<FastVector3Int> originalPoints,
            ConcaveHullStrategy strategy,
            float angleThreshold
        )
        {
            if (hull == null)
            {
                throw new ArgumentNullException(nameof(hull));
            }

#if ENABLE_CONCAVE_HULL_STATS
            if (TryGetTrackedHullRepairStats(hull, out ConcaveHullRepairStats cachedStats))
            {
                return cachedStats;
            }
#endif

            using PooledResource<List<FastVector3Int>> workingHullLease =
                Buffers<FastVector3Int>.List.Get(out List<FastVector3Int> workingHull);
            workingHull.AddRange(hull);

            PooledResource<List<FastVector3Int>> workingOriginalLease = default;
            List<FastVector3Int> workingOriginal = null;
            if (originalPoints != null)
            {
                workingOriginalLease = Buffers<FastVector3Int>.List.Get(out workingOriginal);
                workingOriginal.AddRange(originalPoints);
            }

            try
            {
                ConcaveHullRepairStats stats = new(workingHull.Count, workingOriginal?.Count ?? 0);
                MaybeRepairConcaveCorners(
                    workingHull,
                    workingOriginal,
                    strategy,
                    angleThreshold,
                    stats
                );
                stats.MarkFinalHullCount(workingHull.Count);
                return stats;
            }
            finally
            {
                workingOriginalLease.Dispose();
            }
        }
#endif

        private static void RemoveDuplicateVertices(
            List<FastVector3Int> hull
#if ENABLE_CONCAVE_HULL_STATS
            ,
            ConcaveHullRepairStats stats
#endif
        )
        {
            if (hull == null || hull.Count <= 1)
            {
#if ENABLE_CONCAVE_HULL_STATS
                stats?.MarkFinalHullCount(hull?.Count ?? 0);
#endif
                return;
            }

            using PooledResource<HashSet<FastVector3Int>> seenResource =
                Buffers<FastVector3Int>.HashSet.Get(out HashSet<FastVector3Int> seen);
            int index = 0;
            while (index < hull.Count)
            {
                FastVector3Int vertex = hull[index];
                if (!seen.Add(vertex))
                {
                    hull.RemoveAt(index);
#if ENABLE_CONCAVE_HULL_STATS
                    stats?.IncrementDuplicateRemovals();
#endif
                    continue;
                }

                ++index;
            }

#if ENABLE_CONCAVE_HULL_STATS
            stats?.MarkFinalHullCount(hull.Count);
#endif
        }

        public enum OrientationType
        {
            Colinear = 0,
            Clockwise = 1,
            Counterclockwise = 2,
        }

#if ENABLE_CONCAVE_HULL_STATS
        public sealed class ConcaveHullRepairStats
        {
            public ConcaveHullRepairStats(int startHullCount, int originalPointsCount)
            {
                StartHullCount = startHullCount;
                OriginalPointsCount = originalPointsCount;
            }

            public int StartHullCount { get; }
            public int OriginalPointsCount { get; }
            public int FinalHullCount { get; private set; }
            public int AxisCornerInsertions { get; private set; }
            public int AxisPathInsertions { get; private set; }
            public int CandidateConnections { get; private set; }
            public int DuplicateRemovals { get; private set; }
            public int DiagonalPruned { get; private set; }
            public int AxisNeighborVisits { get; private set; }
            public int MaxFrontierSize { get; private set; }

            internal ConcaveHullRepairStats Clone()
            {
                ConcaveHullRepairStats clone = new(StartHullCount, OriginalPointsCount)
                {
                    FinalHullCount = FinalHullCount,
                    AxisCornerInsertions = AxisCornerInsertions,
                    AxisPathInsertions = AxisPathInsertions,
                    CandidateConnections = CandidateConnections,
                    DuplicateRemovals = DuplicateRemovals,
                    DiagonalPruned = DiagonalPruned,
                    AxisNeighborVisits = AxisNeighborVisits,
                    MaxFrontierSize = MaxFrontierSize,
                };
                return clone;
            }

            internal void IncrementAxisCornerInsertions()
            {
                AxisCornerInsertions++;
            }

            internal void IncrementAxisPathInsertions(int amount)
            {
                if (0 < amount)
                {
                    AxisPathInsertions += amount;
                }
            }

            internal void IncrementCandidateConnections()
            {
                CandidateConnections++;
            }

            internal void IncrementDuplicateRemovals()
            {
                DuplicateRemovals++;
            }

            internal void IncrementDiagonalPruned()
            {
                DiagonalPruned++;
            }

            internal void IncrementAxisNeighborVisits()
            {
                AxisNeighborVisits++;
            }

            internal void MaybeRecordFrontierSize(int size)
            {
                if (MaxFrontierSize < size)
                {
                    MaxFrontierSize = size;
                }
            }

            internal void MarkFinalHullCount(int count)
            {
                FinalHullCount = count;
            }
        }
#endif
    }
}
