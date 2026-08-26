// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using Extension;
    using UnityEngine;
    using Utils;

    /// <summary>
    /// Immutable 2D spatial tree that partitions space into quadrants for efficient range and area queries.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// // Build tree from points using position transformer (recommended)
    /// QuadTree2D<Vector2> tree = new QuadTree2D<Vector2>(points, p => p);
    ///
    /// // Or build from pre-constructed entries for zero-allocation in hot paths
    /// QuadTree2D<Vector2>.Entry[] entries = new QuadTree2D<Vector2>.Entry[points.Length];
    /// for (int i = 0; i < points.Length; i++)
    /// {
    ///     entries[i] = new QuadTree2D<Vector2>.Entry(points[i], points[i]);
    /// }
    /// QuadTree2D<Vector2> tree = new QuadTree2D<Vector2>(entries);
    ///
    /// // Query with pooled list for zero allocation
    /// using var lease = Buffers<Vector2>.List.Get(out List<Vector2> results);
    /// tree.GetElementsInRange(playerPosition, 6f, results);
    /// ]]></code>
    /// </example>
    /// <typeparam name="T">Element type contained in the tree.</typeparam>
    /// <remarks>
    /// Pros: Excellent query performance for static data, low allocations for repeated queries, deterministic iteration.
    /// Cons: Immutable structure; rebuild when positions change. Prefer <c>SpatialHash2D</c> for frequently moving, uniformly distributed data.
    /// Semantics: For identical input data and queries, QuadTree2D and KdTree2D (balanced/unbalanced)
    /// produce the same results; the primary difference is performance and memory layout. RTree2D differs by indexing rectangles.
    /// Usage: Build once from points, then call <see cref="GetElementsInRange(UnityEngine.Vector2,float,System.Collections.Generic.List{T},float)"/> or <see cref="GetElementsInBounds(UnityEngine.Bounds,System.Collections.Generic.List{T})"/>.
    /// </remarks>
    [Serializable]
    public sealed class QuadTree2D<T> : ISpatialTree2D<T>
    {
        private const int NumChildren = 4;

        /// <summary>
        /// Default bucket size for leaves before subdivision.
        /// </summary>
        public const int DefaultBucketSize = 12;

        public readonly ImmutableArray<T> elements;

        /// <summary>
        /// Gets the overall bounding box of the tree.
        /// </summary>
        public Bounds Boundary => _bounds;

        private readonly Bounds _bounds;
        private readonly Entry[] _entries;
        private readonly int[] _indices;
        private readonly QuadTreeNode _head;

        /// <summary>
        /// Builds a QuadTree from elements using a transformer to extract 2D positions.
        /// </summary>
        /// <param name="points">Source elements.</param>
        /// <param name="elementTransformer">Maps element to its 2D position.</param>
        /// <param name="boundary">Optional precomputed bounds. If null, bounds are computed from points.</param>
        /// <param name="bucketSize">Max elements in a leaf before subdividing. Minimum 1.</param>
        /// <exception cref="ArgumentNullException">Thrown when points or elementTransformer are null.</exception>
        public QuadTree2D(
            IEnumerable<T> points,
            Func<T, Vector2> elementTransformer,
            Bounds? boundary = null,
            int bucketSize = DefaultBucketSize
        )
        {
            if (elementTransformer is null)
            {
                throw new ArgumentNullException(nameof(elementTransformer));
            }

            elements =
                points?.ToImmutableArray() ?? throw new ArgumentNullException(nameof(points));

            int elementCount = elements.Length;
            _entries = elementCount == 0 ? Array.Empty<Entry>() : new Entry[elementCount];
            _indices = elementCount == 0 ? Array.Empty<int>() : new int[elementCount];

            Bounds bounds = boundary ?? default;
            bool anyPoints = boundary.HasValue;

            for (int i = 0; i < elementCount; ++i)
            {
                T element = elements[i];
                Vector2 position = elementTransformer(element);
                _entries[i] = new Entry(element, position);
                if (anyPoints)
                {
                    bounds.Encapsulate(position);
                }
                else
                {
                    bounds = new Bounds(position, new Vector3(0f, 0f, 1f));
                    anyPoints = true;
                }

                _indices[i] = i;
            }

            if (anyPoints)
            {
                Vector3 size = bounds.size;
                const float minSize = 0.001f;
                if (size.x < minSize)
                {
                    size.x = minSize;
                }
                if (size.y < minSize)
                {
                    size.y = minSize;
                }
                size.z = 1f;
                bounds.size = size;
            }

            _bounds = bounds;

            if (elementCount == 0)
            {
                _head = QuadTreeNode.CreateLeaf(_bounds, 0, 0);
                return;
            }

            bucketSize = Math.Max(1, bucketSize);
            // SystemArrayPool, not WallstopArrayPool: elementCount is a runtime collection size, and
            // WallstopArrayPool keeps a permanent bucket per distinct size -- its own docs call
            // Get(collection.Count) an unbounded leak. SystemArrayPool is this package's
            // scoped-handle wrapper over the shared pool, so it still disposes through PooledArray
            // with no try/finally. clearArray is false because the build writes every slot before
            // reading it, which is what the previous ArrayPool.Shared.Rent already relied on.
            using PooledArray<int> scratchLease = SystemArrayPool<int>.Get(
                elementCount,
                clearArray: false,
                out int[] scratch
            );
            _head = BuildNode(_bounds, 0, elementCount, bucketSize, scratch);
        }

        /// <summary>
        /// Builds a QuadTree directly from entries containing values and positions.
        /// </summary>
        /// <param name="entries">Collection of values with positions.</param>
        /// <param name="boundary">Optional precomputed bounds. If null, bounds are computed from entries.</param>
        /// <param name="bucketSize">Max elements in a leaf before subdividing. Minimum 1.</param>
        /// <exception cref="ArgumentNullException">Thrown when entries is null.</exception>
        public QuadTree2D(
            IEnumerable<Entry> entries,
            Bounds? boundary = null,
            int bucketSize = DefaultBucketSize
        )
        {
            if (entries is null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            using PooledResource<List<Entry>> entryListResource = Buffers<Entry>.List.Get(
                out List<Entry> entryList
            );
            entryList.AddRange(entries);
            int elementCount = entryList.Count;
            if (elementCount == 0)
            {
                elements = ImmutableArray<T>.Empty;
                _entries = Array.Empty<Entry>();
                _indices = Array.Empty<int>();
                _bounds = boundary ?? default;
                _head = QuadTreeNode.CreateLeaf(_bounds, 0, 0);
                return;
            }

            _entries = new Entry[elementCount];
            _indices = new int[elementCount];
            ImmutableArray<T>.Builder builder = ImmutableArray.CreateBuilder<T>(elementCount);
            Bounds bounds = boundary ?? default;
            bool anyPoints = boundary.HasValue;
            for (int i = 0; i < elementCount; ++i)
            {
                Entry entry = entryList[i];
                _entries[i] = entry;
                builder.Add(entry.value);
                Vector2 position = entry.position;
                if (anyPoints)
                {
                    bounds.Encapsulate(position);
                }
                else
                {
                    bounds = new Bounds(position, new Vector3(0f, 0f, 1f));
                    anyPoints = true;
                }

                _indices[i] = i;
            }

            if (anyPoints)
            {
                Vector3 size = bounds.size;
                const float minSize = 0.001f;
                if (size.x < minSize)
                {
                    size.x = minSize;
                }

                if (size.y < minSize)
                {
                    size.y = minSize;
                }

                size.z = 1f;
                bounds.size = size;
            }

            elements = builder.MoveToImmutable();
            _bounds = bounds;
            bucketSize = Math.Max(1, bucketSize);
            // SystemArrayPool, not WallstopArrayPool: elementCount is a runtime collection size, and
            // WallstopArrayPool keeps a permanent bucket per distinct size -- its own docs call
            // Get(collection.Count) an unbounded leak. SystemArrayPool is this package's
            // scoped-handle wrapper over the shared pool, so it still disposes through PooledArray
            // with no try/finally. clearArray is false because the build writes every slot before
            // reading it, which is what the previous ArrayPool.Shared.Rent already relied on.
            using PooledArray<int> scratchLease = SystemArrayPool<int>.Get(
                elementCount,
                clearArray: false,
                out int[] scratch
            );
            _head = BuildNode(_bounds, 0, elementCount, bucketSize, scratch);
        }

        private QuadTreeNode BuildNode(
            Bounds boundary,
            int startIndex,
            int count,
            int bucketSize,
            int[] scratch
        )
        {
            if (count <= 0)
            {
                return QuadTreeNode.CreateLeaf(boundary, startIndex, 0);
            }

            if (count <= bucketSize)
            {
                return QuadTreeNode.CreateLeaf(boundary, startIndex, count);
            }

            Span<int> counts = stackalloc int[NumChildren];
            Span<int> starts = stackalloc int[NumChildren];
            Span<int> next = stackalloc int[NumChildren];

            Span<int> source = _indices.AsSpan(startIndex, count);
            Span<int> temp = scratch.AsSpan(0, count);

            Vector3 quadrantSize = boundary.size / 2f;
            quadrantSize.z = 1f;
            Vector3 halfQuadrantSize = quadrantSize / 2f;
            Vector3 boundaryCenter = boundary.center;
            float centerX = boundaryCenter.x;
            float centerY = boundaryCenter.y;

            Entry[] entries = _entries;
            for (int i = 0; i < count; ++i)
            {
                int entryIndex = source[i];
                Vector2 position = entries[entryIndex].position;
                bool east = centerX < position.x;
                bool north = centerY <= position.y;
                int quadrant = east
                    ? north
                        ? 1
                        : 2
                    : north
                        ? 0
                        : 3;
                counts[quadrant]++;
            }

            int maxChildCount = 0;
            int running = 0;
            for (int q = 0; q < NumChildren; ++q)
            {
                starts[q] = running;
                next[q] = running;
                running += counts[q];
                if (maxChildCount < counts[q])
                {
                    maxChildCount = counts[q];
                }
            }

            if (maxChildCount == count)
            {
                return QuadTreeNode.CreateLeaf(boundary, startIndex, count);
            }

            for (int i = 0; i < count; ++i)
            {
                int entryIndex = source[i];
                Vector2 position = entries[entryIndex].position;
                bool east = centerX < position.x;
                bool north = centerY <= position.y;
                int quadrant = east
                    ? north
                        ? 1
                        : 2
                    : north
                        ? 0
                        : 3;
                int destination = next[quadrant]++;
                temp[destination] = entryIndex;
            }

            temp.CopyTo(source);

            Span<Bounds> quadrants = stackalloc Bounds[NumChildren];
            quadrants[0] = new Bounds(
                new Vector3(centerX - halfQuadrantSize.x, centerY + halfQuadrantSize.y),
                quadrantSize
            );
            quadrants[1] = new Bounds(
                new Vector3(centerX + halfQuadrantSize.x, centerY + halfQuadrantSize.y),
                quadrantSize
            );
            quadrants[2] = new Bounds(
                new Vector3(centerX + halfQuadrantSize.x, centerY - halfQuadrantSize.y),
                quadrantSize
            );
            quadrants[3] = new Bounds(
                new Vector3(centerX - halfQuadrantSize.x, centerY - halfQuadrantSize.y),
                quadrantSize
            );

            QuadTreeNode[] children = new QuadTreeNode[NumChildren];
            for (int q = 0; q < NumChildren; ++q)
            {
                int childCount = counts[q];
                if (childCount <= 0)
                {
                    continue;
                }

                int childStart = startIndex + starts[q];
                children[q] = BuildNode(quadrants[q], childStart, childCount, bucketSize, scratch);
            }

            return QuadTreeNode.CreateInternal(boundary, children, startIndex, count);
        }

        /// <summary>
        /// Finds all elements within distance <paramref name="range"/> of <paramref name="position"/>.
        /// </summary>
        /// <param name="position">Query center.</param>
        /// <param name="range">Query radius.</param>
        /// <param name="elementsInRange">Destination list which is cleared before use.</param>
        /// <param name="minimumRange">Optional inner exclusion radius.</param>
        /// <returns>The destination list, for chaining.</returns>
        /// <example>
        /// <code><![CDATA[
        /// QuadTree2D<Enemy> tree = new QuadTree2D<Enemy>(enemies, e => e.transform.position);
        /// using var lease = Buffers<Enemy>.List.Get(out List<Enemy> results);
        /// tree.GetElementsInRange(playerPos, 10f, results);
        /// ]]></code>
        /// </example>
        public List<T> GetElementsInRange(
            Vector2 position,
            float range,
            List<T> elementsInRange,
            float minimumRange = 0
        )
        {
            elementsInRange.Clear();
            if (range < 0f || _head._count <= 0)
            {
                return elementsInRange;
            }

            Bounds bounds = new(position, new Vector3(range * 2f, range * 2f, 1f));

            if (!bounds.FastIntersects2D(_bounds))
            {
                return elementsInRange;
            }

            using PooledResource<Stack<QuadTreeNode>> nodesToVisitResource =
                Buffers<QuadTreeNode>.Stack.Get(out Stack<QuadTreeNode> nodesToVisit);
            nodesToVisit.Push(_head);

            Entry[] entries = _entries;
            int[] indices = _indices;
            float rangeSquared = range * range;
            bool hasMinimumRange = 0f < minimumRange;
            float minimumRangeSquared = minimumRange * minimumRange;

            while (nodesToVisit.TryPop(out QuadTreeNode currentNode))
            {
                if (currentNode is null || currentNode._count <= 0)
                {
                    continue;
                }

                if (!bounds.FastIntersects2D(currentNode.boundary))
                {
                    continue;
                }

                if (currentNode.isTerminal || bounds.FastContains2D(currentNode.boundary))
                {
                    int start = currentNode._startIndex;
                    int end = start + currentNode._count;
                    for (int i = start; i < end; ++i)
                    {
                        Entry entry = entries[indices[i]];
                        float squareDistance = (entry.position - position).sqrMagnitude;
                        if (rangeSquared < squareDistance)
                        {
                            continue;
                        }

                        if (hasMinimumRange && squareDistance <= minimumRangeSquared)
                        {
                            continue;
                        }

                        elementsInRange.Add(entry.value);
                    }

                    continue;
                }

                QuadTreeNode[] childNodes = currentNode._children;
                for (int i = 0; i < childNodes.Length; ++i)
                {
                    QuadTreeNode child = childNodes[i];
                    if (child is null || child._count <= 0)
                    {
                        continue;
                    }

                    if (bounds.FastIntersects2D(child.boundary))
                    {
                        nodesToVisit.Push(child);
                    }
                }
            }

            return elementsInRange;
        }

        /// <summary>
        /// Finds all elements whose positions lie within the specified bounds.
        /// </summary>
        /// <param name="bounds">Axis-aligned query bounds.</param>
        /// <param name="elementsInBounds">Destination list which is cleared before use.</param>
        /// <returns>The destination list, for chaining.</returns>
        public List<T> GetElementsInBounds(Bounds bounds, List<T> elementsInBounds)
        {
            elementsInBounds.Clear();
            if (_head._count <= 0 || !bounds.FastIntersects2D(_bounds))
            {
                return elementsInBounds;
            }
            using PooledResource<Stack<QuadTreeNode>> stackResource =
                Buffers<QuadTreeNode>.Stack.Get(out Stack<QuadTreeNode> nodesToVisit);
            nodesToVisit.Push(_head);

            Entry[] entries = _entries;
            int[] indices = _indices;

            while (nodesToVisit.TryPop(out QuadTreeNode currentNode))
            {
                if (currentNode is null || currentNode._count <= 0)
                {
                    continue;
                }

                if (bounds.FastContains2D(currentNode.boundary))
                {
                    int start = currentNode._startIndex;
                    int end = start + currentNode._count;
                    for (int i = start; i < end; ++i)
                    {
                        elementsInBounds.Add(entries[indices[i]].value);
                    }

                    continue;
                }

                if (currentNode.isTerminal)
                {
                    int start = currentNode._startIndex;
                    int end = start + currentNode._count;
                    for (int i = start; i < end; ++i)
                    {
                        Entry entry = entries[indices[i]];
                        if (bounds.FastContains2D(entry.position))
                        {
                            elementsInBounds.Add(entry.value);
                        }
                    }

                    continue;
                }

                QuadTreeNode[] childNodes = currentNode._children;
                for (int i = 0; i < childNodes.Length; ++i)
                {
                    QuadTreeNode child = childNodes[i];
                    if (child is null || child._count <= 0)
                    {
                        continue;
                    }

                    if (bounds.FastIntersects2D(child.boundary))
                    {
                        nodesToVisit.Push(child);
                    }
                }
            }

            return elementsInBounds;
        }

        /// <summary>
        /// Returns an approximate set of the nearest <paramref name="count"/> neighbors to <paramref name="position"/>.
        /// </summary>
        /// <remarks>
        /// Faster than exact kNN on the tree by prioritizing closer nodes; suitable for gameplay proximity needs.
        /// </remarks>
        public List<T> GetApproximateNearestNeighbors(
            Vector2 position,
            int count,
            List<T> nearestNeighbors
        )
        {
            nearestNeighbors.Clear();

            if (count <= 0 || _head._count == 0)
            {
                return nearestNeighbors;
            }

            using PooledResource<Stack<QuadTreeNode>> nodeBufferResource =
                Buffers<QuadTreeNode>.Stack.Get(out Stack<QuadTreeNode> nodeBuffer);
            nodeBuffer.Push(_head);

            using PooledResource<List<QuadTreeNode>> childrenBufferResource =
                Buffers<QuadTreeNode>.List.Get(out List<QuadTreeNode> childrenBuffer);
            using PooledResource<HashSet<T>> nearestNeighborBufferResource = Buffers<T>.HashSet.Get(
                out HashSet<T> nearestNeighborBuffer
            );
            using PooledResource<List<Neighbor>> neighborCandidatesResource =
                Buffers<Neighbor>.List.Get(out List<Neighbor> neighborCandidates);

            Entry[] entries = _entries;
            int[] indices = _indices;

            QuadTreeNode current = _head;

            while (!current.isTerminal)
            {
                childrenBuffer.Clear();
                QuadTreeNode[] childNodes = current._children;
                for (int i = 0; i < childNodes.Length; ++i)
                {
                    QuadTreeNode child = childNodes[i];
                    if (child is not null && 0 < child._count)
                    {
                        childrenBuffer.Add(child);
                    }
                }

                if (childrenBuffer.Count == 0)
                {
                    break;
                }

                SortChildrenByDistance(childrenBuffer, position);
                for (int i = childrenBuffer.Count - 1; 0 <= i; --i)
                {
                    nodeBuffer.Push(childrenBuffer[i]);
                }

                current = childrenBuffer[0];
                if (current._count <= count)
                {
                    break;
                }
            }

            while (
                nearestNeighborBuffer.Count < count && nodeBuffer.TryPop(out QuadTreeNode selected)
            )
            {
                if (selected is null || selected._count <= 0)
                {
                    continue;
                }

                int startIndex = selected._startIndex;
                int endIndex = startIndex + selected._count;
                for (int i = startIndex; i < endIndex; ++i)
                {
                    Entry entry = entries[indices[i]];
                    if (!nearestNeighborBuffer.Add(entry.value))
                    {
                        continue;
                    }

                    float sqrDistance = (entry.position - position).sqrMagnitude;
                    neighborCandidates.Add(new Neighbor(entry.value, sqrDistance));
                }
            }

            if (count < neighborCandidates.Count)
            {
                neighborCandidates.Sort(NeighborComparer.Instance);
                neighborCandidates.RemoveRange(count, neighborCandidates.Count - count);
            }

            nearestNeighbors.Clear();
            for (int i = 0; i < neighborCandidates.Count && i < count; ++i)
            {
                nearestNeighbors.Add(neighborCandidates[i].value);
            }

            return nearestNeighbors;
        }

        private static void SortChildrenByDistance(List<QuadTreeNode> nodes, Vector2 searchPosition)
        {
            for (int i = 1; i < nodes.Count; ++i)
            {
                QuadTreeNode value = nodes[i];
                float valueDistance = GetSqrDistance(value, searchPosition);
                int j = i - 1;
                while (0 <= j && valueDistance < GetSqrDistance(nodes[j], searchPosition))
                {
                    nodes[j + 1] = nodes[j];
                    --j;
                }

                nodes[j + 1] = value;
            }
        }

        private static float GetSqrDistance(QuadTreeNode node, Vector2 position)
        {
            return ((Vector2)node.boundary.center - position).sqrMagnitude;
        }

        private sealed class NeighborComparer : IComparer<Neighbor>
        {
            internal static readonly NeighborComparer Instance = new();

            public int Compare(Neighbor x, Neighbor y)
            {
                return x.sqrDistance.CompareTo(y.sqrDistance);
            }
        }

        /// <summary>
        /// Represents a value and its position used to construct the tree directly.
        /// </summary>
        [Serializable]
        public readonly struct Entry
        {
            public readonly T value;
            public readonly Vector2 position;

            public Entry(T value, Vector2 position)
            {
                this.value = value;
                this.position = position;
            }
        }

        private readonly struct Neighbor
        {
            public readonly T value;
            public readonly float sqrDistance;

            public Neighbor(T value, float sqrDistance)
            {
                this.value = value;
                this.sqrDistance = sqrDistance;
            }
        }

        [Serializable]
        public sealed class QuadTreeNode
        {
            public readonly Bounds boundary;
            internal readonly QuadTreeNode[] _children;
            internal readonly int _startIndex;
            internal readonly int _count;
            public readonly bool isTerminal;

            private QuadTreeNode(
                Bounds boundary,
                int startIndex,
                int count,
                bool isTerminal,
                QuadTreeNode[] children
            )
            {
                this.boundary = boundary;
                _startIndex = startIndex;
                _count = count;
                this.isTerminal = isTerminal;
                _children = children ?? Array.Empty<QuadTreeNode>();
            }

            internal static QuadTreeNode CreateLeaf(Bounds boundary, int startIndex, int count)
            {
                return new QuadTreeNode(
                    boundary,
                    startIndex,
                    count,
                    true,
                    Array.Empty<QuadTreeNode>()
                );
            }

            internal static QuadTreeNode CreateInternal(
                Bounds boundary,
                QuadTreeNode[] children,
                int startIndex,
                int count
            )
            {
                return new QuadTreeNode(boundary, startIndex, count, false, children);
            }
        }
    }
}
