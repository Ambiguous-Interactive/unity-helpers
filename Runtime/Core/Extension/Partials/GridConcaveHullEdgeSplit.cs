// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// ReSharper disable once CheckNamespace
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using DataStructure;
    using DataStructure.Adapters;
    using UnityEngine;
    using Utils;

    // GridConcaveHullEdgeSplit.cs - Edge-splitting algorithm implementation
    // See GeometryConcaveHull.cs for full concave hull architecture documentation
    /// <summary>
    /// Edge-splitting concave hull builders plus supporting structures.
    /// Starts with convex hull and recursively splits longest edges with interior points.
    /// </summary>
    public static partial class UnityExtensions
    {
        public static List<Vector2> BuildConcaveHullEdgeSplit(
            this IReadOnlyCollection<Vector2> points,
            int bucketSize = 40,
            float angleThreshold = 90f
        )
        {
            int clampedBucketSize = Math.Max(1, bucketSize);
            float effectiveAngleThreshold = clampedBucketSize <= 1 ? 0f : angleThreshold;
            ConcaveHullOptions options = ConcaveHullOptions
                .Default.WithStrategy(ConcaveHullStrategy.EdgeSplit)
                .WithBucketSize(clampedBucketSize)
                .WithAngleThreshold(effectiveAngleThreshold);
            return points.BuildConcaveHull(options);
        }

        private readonly struct HullEdge
        {
            public readonly float edgeLength;

            public readonly FastVector3Int from;
            public readonly FastVector3Int to;

            public readonly Vector2 fromWorld;
            public readonly Vector2 toWorld;

            private readonly Grid _grid;

            public HullEdge(FastVector3Int from, FastVector3Int to, Grid grid)
            {
                this.from = from;
                this.to = to;
                _grid = grid;
                fromWorld = grid.CellToWorld(from);
                toWorld = grid.CellToWorld(to);
                edgeLength = (fromWorld - toWorld).sqrMagnitude;
            }

            public bool Intersects(HullEdge other)
            {
                return UnityExtensions.Intersects(
                    fromWorld,
                    toWorld,
                    other.fromWorld,
                    other.toWorld
                );
            }

            public float LargestAngle(FastVector3Int point)
            {
                Vector2 worldPoint = _grid.CellToWorld(point);
                float angleFrom = Vector2.Angle(toWorld - fromWorld, worldPoint - fromWorld);
                float angleTo = Vector2.Angle(fromWorld - toWorld, worldPoint - toWorld);
                return Math.Max(angleFrom, angleTo);
            }
        }

        private sealed class ConcaveHullComparer : IComparer<HullEdge>
        {
            public static readonly ConcaveHullComparer Instance = new();

            private ConcaveHullComparer() { }

            public int Compare(HullEdge lhs, HullEdge rhs)
            {
                int comparison = lhs.edgeLength.CompareTo(rhs.edgeLength);
                if (comparison != 0)
                {
                    return comparison;
                }

                comparison = lhs.from.CompareTo(rhs.from);
                if (comparison != 0)
                {
                    return comparison;
                }

                return lhs.to.CompareTo(rhs.to);
            }
        }

        /// <summary>
        /// Grid-based edge-splitting concave hull.
        /// </summary>
        [Obsolete("Use BuildConcaveHullEdgeSplit or BuildConcaveHull with options.")]
        public static List<FastVector3Int> BuildConcaveHull3(
            this IReadOnlyCollection<FastVector3Int> gridPositions,
            Grid grid,
            int bucketSize = 40,
            float angleThreshold = 90f
        )
        {
            using PooledResource<List<FastVector3Int>> originalGridPositionsBuffer =
                Buffers<FastVector3Int>.List.Get(out List<FastVector3Int> originalGridPositions);
            originalGridPositions.AddRange(gridPositions);

            List<FastVector3Int> convexHull = gridPositions.BuildConvexHull(grid);
            using (
                PooledResource<HashSet<FastVector3Int>> uniqueBuffer =
                    Buffers<FastVector3Int>.HashSet.Get(out HashSet<FastVector3Int> unique)
            )
            {
                foreach (FastVector3Int p in originalGridPositions)
                {
                    unique.Add(p);
                }
                if (unique.Count <= 4)
                {
                    return convexHull;
                }
            }

            if (AreAllPointsOnHullEdges(originalGridPositions, convexHull))
            {
                return convexHull;
            }
            using PooledResource<List<HullEdge>> concaveHullEdgesResource =
                Buffers<HullEdge>.List.Get(out List<HullEdge> concaveHullEdges);
            if (concaveHullEdges.Capacity < convexHull.Count)
            {
                concaveHullEdges.Capacity = convexHull.Count;
            }

            using PooledResource<SortedSet<HullEdge>> sortedSetBuffer = SetBuffers<HullEdge>
                .GetSortedSetPool(ConcaveHullComparer.Instance)
                .Get(out SortedSet<HullEdge> data);

            for (int i = 0; i < convexHull.Count; ++i)
            {
                FastVector3Int lhs = convexHull[i];
                FastVector3Int rhs = convexHull[(i + 1) % convexHull.Count];
                HullEdge edge = new(lhs, rhs, grid);
                _ = data.Add(edge);
            }

            using PooledResource<HashSet<FastVector3Int>> remainingPointsBuffer =
                Buffers<FastVector3Int>.HashSet.Get(out HashSet<FastVector3Int> remainingPoints);
            foreach (FastVector3Int gridPosition in originalGridPositions)
            {
                remainingPoints.Add(gridPosition);
            }

            remainingPoints.ExceptWith(convexHull);

            Bounds? maybeBounds = CalculateWorldBounds(originalGridPositions, grid);
            if (maybeBounds == null)
            {
                throw new ArgumentException(nameof(gridPositions));
            }

            using PooledResource<List<QuadTree2D<FastVector3Int>.Entry>> quadTreeEntriesResource =
                Buffers<QuadTree2D<FastVector3Int>.Entry>.List.Get(
                    out List<QuadTree2D<FastVector3Int>.Entry> quadTreeEntries
                );
            foreach (FastVector3Int gridPosition in originalGridPositions)
            {
                Vector2 worldPosition = grid.CellToWorld(gridPosition);
                quadTreeEntries.Add(
                    new QuadTree2D<FastVector3Int>.Entry(gridPosition, worldPosition)
                );
            }

            QuadTree2D<FastVector3Int> quadTree = new(
                quadTreeEntries,
                maybeBounds.Value,
                bucketSize: bucketSize
            );
            using PooledResource<List<FastVector3Int>> neighborsBuffer =
                Buffers<FastVector3Int>.List.Get(out List<FastVector3Int> neighbors);
            if (neighbors.Capacity < bucketSize)
            {
                neighbors.Capacity = bucketSize;
            }
            int iterations = 0;
            int maxIterations = Math.Max(32, originalGridPositions.Count * 16);
            while (0 < data.Count)
            {
                HullEdge edge = data.Max;
                _ = data.Remove(edge);

                Vector2 edgeCenter = edge.fromWorld + (edge.toWorld - edge.fromWorld) / 2;
                quadTree.GetApproximateNearestNeighbors(edgeCenter, bucketSize, neighbors);
                float localMaximumDistance = float.MinValue;
                int usableNeighborCount = 0;
                foreach (FastVector3Int neighbor in neighbors)
                {
                    if (neighbor == edge.to || neighbor == edge.from)
                    {
                        continue;
                    }

                    ++usableNeighborCount;
                    localMaximumDistance = Math.Max(
                        localMaximumDistance,
                        (CellToWorld(neighbor) - edgeCenter).sqrMagnitude
                    );
                }

                if (usableNeighborCount < 2)
                {
                    concaveHullEdges.Add(edge);
                    continue;
                }

                if (edge.edgeLength <= localMaximumDistance)
                {
                    concaveHullEdges.Add(edge);
                    continue;
                }

                float smallestAngle = float.MaxValue;
                FastVector3Int? maybeChosenPoint = null;
                foreach (FastVector3Int remainingPoint in remainingPoints)
                {
                    float maximumAngle = edge.LargestAngle(remainingPoint);
                    if (maximumAngle < smallestAngle)
                    {
                        maybeChosenPoint = remainingPoint;
                        smallestAngle = maximumAngle;
                    }
                }

                if (angleThreshold < smallestAngle)
                {
                    concaveHullEdges.Add(edge);
                    continue;
                }

                if (maybeChosenPoint == null)
                {
                    concaveHullEdges.Add(edge);
                    continue;
                }

                FastVector3Int chosenPoint = maybeChosenPoint.Value;
                HullEdge e2 = new(edge.from, chosenPoint, grid);
                HullEdge e3 = new(chosenPoint, edge.to, grid);
                bool intersects = false;
                foreach (HullEdge convexHullEdge in data)
                {
                    if (convexHullEdge.Intersects(e2))
                    {
                        intersects = true;
                        break;
                    }

                    if (convexHullEdge.Intersects(e3))
                    {
                        intersects = true;
                        break;
                    }
                }

                if (!intersects)
                {
                    foreach (HullEdge concaveHullEdge in concaveHullEdges)
                    {
                        if (concaveHullEdge.Intersects(e2))
                        {
                            intersects = true;
                            break;
                        }

                        if (concaveHullEdge.Intersects(e3))
                        {
                            intersects = true;
                            break;
                        }
                    }
                }

                if (!intersects)
                {
                    _ = data.Add(e2);
                    _ = data.Add(e3);
                    _ = remainingPoints.Remove(maybeChosenPoint.Value);
                }
                else
                {
                    concaveHullEdges.Add(edge);
                }

                ++iterations;
                if (maxIterations < iterations)
                {
                    // Safety: avoid runaway refinement by flushing remaining edges to concave set
                    concaveHullEdges.AddRange(data);
                    break;
                }
            }

            List<FastVector3Int> concaveHull = new(concaveHullEdges.Count);
            HullEdge current = concaveHullEdges[0];
            concaveHullEdges.RemoveAtSwapBack(0);
            concaveHull.Add(current.from);
            while (0 < concaveHullEdges.Count)
            {
                FastVector3Int to = current.to;
                int nextIndex = -1;
                for (int i = 0; i < concaveHullEdges.Count; ++i)
                {
                    HullEdge edge = concaveHullEdges[i];
                    if (edge.from == to)
                    {
                        nextIndex = i;
                        break;
                    }
                }

                if (nextIndex < 0)
                {
                    // Try to recover by using a reversed edge if available.
                    int reverseIndex = -1;
                    for (int i = 0; i < concaveHullEdges.Count; ++i)
                    {
                        HullEdge edge = concaveHullEdges[i];
                        if (edge.to == to)
                        {
                            reverseIndex = i;
                            break;
                        }
                    }

                    if (0 <= reverseIndex)
                    {
                        HullEdge reversed = new(
                            concaveHullEdges[reverseIndex].to,
                            concaveHullEdges[reverseIndex].from,
                            grid
                        );
                        concaveHullEdges.RemoveAtSwapBack(reverseIndex);
                        current = reversed;
                        concaveHull.Add(current.from);
                        continue;
                    }

                    // No connecting edge found; break to avoid infinite loop.
                    break;
                }
                current = concaveHullEdges[nextIndex];
                concaveHullEdges.RemoveAtSwapBack(nextIndex);
                concaveHull.Add(current.from);
            }

            PruneColinearOnHull(concaveHull);
            return concaveHull;

            Vector2 CellToWorld(FastVector3Int cell) => grid.CellToWorld(cell);
        }

        private static Bounds? CalculateWorldBounds(
            IEnumerable<FastVector3Int> positions,
            Grid grid
        )
        {
            bool any = false;
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            foreach (FastVector3Int position in positions)
            {
                Vector3 world = grid.CellToWorld(position);
                float x = world.x;
                float y = world.y;
                if (x < minX)
                {
                    minX = x;
                }

                if (maxX < x)
                {
                    maxX = x;
                }

                if (y < minY)
                {
                    minY = y;
                }

                if (maxY < y)
                {
                    maxY = y;
                }

                any = true;
            }

            if (!any)
            {
                return null;
            }

            Vector3 center = new((minX + maxX) / 2f, (minY + maxY) / 2f, 0f);
            Vector3 size = new(maxX - minX, maxY - minY, 1f);
            return new Bounds(center, size);
        }

        // Edge-splitting + kNN queries via QuadTree for Vector2 (port of BuildConcaveHull3)
        private static List<Vector2> BuildConcaveHull3(
            this IReadOnlyCollection<Vector2> input,
            int bucketSize,
            float angleThreshold
        )
        {
            using PooledResource<List<Vector2>> originalRes = Buffers<Vector2>.List.Get(
                out List<Vector2> original
            );
            original.AddRange(input);

            using PooledResource<HashSet<Vector2>> remainingRes = Buffers<Vector2>.HashSet.Get(
                out HashSet<Vector2> remainingPoints
            );
            remainingPoints.UnionWith(original);

            List<Vector2> convexHull = input.BuildConvexHull(includeColinearPoints: false);
            using (
                PooledResource<HashSet<Vector2>> uniqRes = Buffers<Vector2>.HashSet.Get(
                    out HashSet<Vector2> uniq
                )
            )
            {
                foreach (Vector2 p in original)
                {
                    uniq.Add(p);
                }
                if (uniq.Count <= 4)
                {
                    return convexHull;
                }
            }
            remainingPoints.ExceptWith(convexHull);

            using PooledResource<List<HullEdgeV2>> edgeListRes = Buffers<HullEdgeV2>.List.Get(
                out List<HullEdgeV2> concaveHullEdges
            );
            if (concaveHullEdges.Capacity < convexHull.Count)
            {
                concaveHullEdges.Capacity = convexHull.Count;
            }
            using PooledResource<SortedSet<HullEdgeV2>> sortedSetRes = SetBuffers<HullEdgeV2>
                .GetSortedSetPool(ConcaveHullComparerV2.Instance)
                .Get(out SortedSet<HullEdgeV2> data);

            for (int i = 0; i < convexHull.Count; ++i)
            {
                Vector2 lhs = convexHull[i];
                Vector2 rhs = convexHull[(i + 1) % convexHull.Count];
                _ = data.Add(new HullEdgeV2(lhs, rhs));
            }

            Bounds? maybeBounds = input.GetBounds();
            if (maybeBounds == null)
            {
                throw new ArgumentException(nameof(input));
            }

            using PooledResource<List<QuadTree2D<Vector2>.Entry>> entriesRes =
                Buffers<QuadTree2D<Vector2>.Entry>.List.Get(
                    out List<QuadTree2D<Vector2>.Entry> entries
                );
            foreach (Vector2 p in original)
            {
                entries.Add(new QuadTree2D<Vector2>.Entry(p, p));
            }
            QuadTree2D<Vector2> quadTree = new(entries, maybeBounds.Value, bucketSize);
            using PooledResource<List<Vector2>> neighborsRes = Buffers<Vector2>.List.Get(
                out List<Vector2> neighbors
            );
            if (neighbors.Capacity < bucketSize)
            {
                neighbors.Capacity = bucketSize;
            }

            int iterations = 0;
            int maxIterations = Math.Max(32, original.Count * 16);
            while (0 < data.Count)
            {
                HullEdgeV2 edge = data.Max;
                _ = data.Remove(edge);

                Vector2 edgeCenter = edge.from + (edge.to - edge.from) / 2f;
                quadTree.GetApproximateNearestNeighbors(edgeCenter, bucketSize, neighbors);
                float localMaximumDistance = float.MinValue;
                int usableNeighborCount = 0;
                foreach (Vector2 n in neighbors)
                {
                    if (n == edge.to || n == edge.from)
                    {
                        continue;
                    }
                    ++usableNeighborCount;
                    localMaximumDistance = Math.Max(
                        localMaximumDistance,
                        (n - edgeCenter).sqrMagnitude
                    );
                }
                if (usableNeighborCount < 2)
                {
                    concaveHullEdges.Add(edge);
                    continue;
                }
                if (edge.edgeLength <= localMaximumDistance)
                {
                    concaveHullEdges.Add(edge);
                    continue;
                }

                float smallestAngle = float.MaxValue;
                Vector2? maybeChosen = null;
                foreach (Vector2 candidate in remainingPoints)
                {
                    if (candidate == edge.to || candidate == edge.from)
                    {
                        continue;
                    }
                    float angle = edge.LargestAngle(candidate);
                    if (angle < smallestAngle)
                    {
                        smallestAngle = angle;
                        maybeChosen = candidate;
                    }
                }
                if (!maybeChosen.HasValue || angleThreshold < smallestAngle)
                {
                    concaveHullEdges.Add(edge);
                    continue;
                }

                Vector2 chosen = maybeChosen.Value;
                HullEdgeV2 e2 = new(edge.from, chosen);
                HullEdgeV2 e3 = new(chosen, edge.to);
                bool intersects = false;
                foreach (HullEdgeV2 hullEdge in data)
                {
                    if (hullEdge.Intersects(e2) || hullEdge.Intersects(e3))
                    {
                        intersects = true;
                        break;
                    }
                }
                if (!intersects)
                {
                    foreach (HullEdgeV2 hullEdge in concaveHullEdges)
                    {
                        if (hullEdge.Intersects(e2) || hullEdge.Intersects(e3))
                        {
                            intersects = true;
                            break;
                        }
                    }
                }
                if (!intersects)
                {
                    _ = data.Add(e2);
                    _ = data.Add(e3);
                    _ = remainingPoints.Remove(chosen);
                }
                else
                {
                    concaveHullEdges.Add(edge);
                }

                ++iterations;
                if (maxIterations < iterations)
                {
                    concaveHullEdges.AddRange(data);
                    break;
                }
            }

            List<Vector2> result = new(concaveHullEdges.Count);
            if (concaveHullEdges.Count == 0)
            {
                return result;
            }
            HullEdgeV2 current = concaveHullEdges[0];
            concaveHullEdges.RemoveAtSwapBack(0);
            result.Add(current.from);
            while (0 < concaveHullEdges.Count)
            {
                Vector2 to = current.to;
                int nextIndex = -1;
                for (int i = 0; i < concaveHullEdges.Count; ++i)
                {
                    HullEdgeV2 e = concaveHullEdges[i];
                    if (e.from == to)
                    {
                        nextIndex = i;
                        break;
                    }
                }
                if (nextIndex < 0)
                {
                    int reverseIndex = -1;
                    for (int i = 0; i < concaveHullEdges.Count; ++i)
                    {
                        HullEdgeV2 e = concaveHullEdges[i];
                        if (e.to == to)
                        {
                            reverseIndex = i;
                            break;
                        }
                    }
                    if (0 <= reverseIndex)
                    {
                        HullEdgeV2 reversed = new(
                            concaveHullEdges[reverseIndex].to,
                            concaveHullEdges[reverseIndex].from
                        );
                        concaveHullEdges.RemoveAtSwapBack(reverseIndex);
                        current = reversed;
                        result.Add(current.from);
                        continue;
                    }
                    break;
                }
                current = concaveHullEdges[nextIndex];
                concaveHullEdges.RemoveAtSwapBack(nextIndex);
                result.Add(current.from);
            }
            return result;
        }

        private readonly struct HullEdgeV2
        {
            public readonly float edgeLength;
            public readonly Vector2 from;
            public readonly Vector2 to;

            public HullEdgeV2(Vector2 from, Vector2 to)
            {
                this.from = from;
                this.to = to;
                edgeLength = (from - to).sqrMagnitude;
            }

            public bool Intersects(HullEdgeV2 other)
            {
                return UnityExtensions.Intersects(from, to, other.from, other.to);
            }

            public float LargestAngle(Vector2 point)
            {
                float angleFrom = Vector2.Angle(to - from, point - from);
                float angleTo = Vector2.Angle(from - to, point - to);
                return Math.Max(angleFrom, angleTo);
            }
        }

        private sealed class ConcaveHullComparerV2 : IComparer<HullEdgeV2>
        {
            public static readonly ConcaveHullComparerV2 Instance = new();

            private ConcaveHullComparerV2() { }

            public int Compare(HullEdgeV2 lhs, HullEdgeV2 rhs)
            {
                int comparison = lhs.edgeLength.CompareTo(rhs.edgeLength);
                if (comparison != 0)
                {
                    return comparison;
                }
                comparison = lhs.from.x.CompareTo(rhs.from.x);
                if (comparison != 0)
                {
                    return comparison;
                }

                comparison = lhs.from.y.CompareTo(rhs.from.y);
                if (comparison != 0)
                {
                    return comparison;
                }

                comparison = lhs.to.x.CompareTo(rhs.to.x);
                if (comparison != 0)
                {
                    return comparison;
                }

                return lhs.to.y.CompareTo(rhs.to.y);
            }
        }
    }
}
