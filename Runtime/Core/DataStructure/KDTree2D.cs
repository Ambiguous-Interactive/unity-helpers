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
    /// Immutable 2D k-d tree for efficient nearest neighbor, range, and bounds queries.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// KdTree2D<Vector2>.Entry[] entries = points.Select(p => new KdTree2D<Vector2>.Entry(p, p)).ToArray();
    /// KdTree2D<Vector2> tree = KdTree2D<Vector2>.Build(entries);
    /// List<Vector2> neighbors = new List<Vector2>();
    /// tree.GetElementsInRange(queryPosition, 3f, neighbors);
    /// ]]></code>
    /// </example>
    /// <typeparam name="T">Element type contained in the tree.</typeparam>
    /// <remarks>
    /// Pros: Very fast nearest neighbor performance; good for static or batched updates.
    /// Cons: Immutable structure by design; rebuild when positions change frequently.
    /// Semantics: For identical input data and queries, KdTree2D (balanced or unbalanced)
    /// returns the same set of results as QuadTree2D; they differ only in performance characteristics.
    /// </remarks>
    [Serializable]
    public sealed class KdTree2D<T> : ISpatialTree2D<T>
    {
        private const float MinimumNodeSize = 0.001f;
        private const int SmallPartitionThreshold = 32;

        /// <summary>
        /// Default bucket size for leaves before stopping recursion.
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
        private readonly KdTreeNode _head;
        private readonly bool _balanced;
        private readonly int _bucketSize;

        /// <summary>
        /// Builds a 2D k-d tree from elements using a transformer to extract 2D positions.
        /// </summary>
        /// <param name="points">Source elements.</param>
        /// <param name="elementTransformer">Maps element to its 2D position.</param>
        /// <param name="bucketSize">Max elements per leaf. Minimum 1.</param>
        /// <param name="balanced">If true, builds a balanced tree by median selection; otherwise uses a quick split strategy.</param>
        /// <exception cref="ArgumentNullException">Thrown when points or elementTransformer are null.</exception>
        public KdTree2D(
            IEnumerable<T> points,
            Func<T, Vector2> elementTransformer,
            int bucketSize = DefaultBucketSize,
            bool balanced = true
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
            _balanced = balanced;
            _bucketSize = Math.Max(1, bucketSize);

            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;

            for (int i = 0; i < elementCount; ++i)
            {
                T element = elements[i];
                Vector2 position = elementTransformer(element);
                _entries[i] = new Entry(element, position);

                if (position.x < minX)
                {
                    minX = position.x;
                }
                if (position.y < minY)
                {
                    minY = position.y;
                }
                if (maxX < position.x)
                {
                    maxX = position.x;
                }
                if (maxY < position.y)
                {
                    maxY = position.y;
                }

                _indices[i] = i;
            }

            Bounds bounds = CreateBounds(minX, maxX, minY, maxY);

            if (elementCount == 0)
            {
                _bounds = bounds;
                _head = KdTreeNode.CreateLeaf(_bounds, 0, 0);
                return;
            }

            KdTreeNode root;
            if (_balanced)
            {
                root = BuildBalanced(0, elementCount, depth: 0);
            }
            else
            {
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
                root = BuildUnbalanced(0, elementCount, splitOnXAxis: true, scratch);
            }

            _head = root;
            _bounds = root.boundary;
        }

        private KdTreeNode BuildBalanced(int startIndex, int count, int depth)
        {
            if (count <= _bucketSize)
            {
                Bounds leafBounds = CalculateLeafBounds(startIndex, count);
                return KdTreeNode.CreateLeaf(leafBounds, startIndex, count);
            }

            bool splitOnXAxis = (depth & 1) == 0;
            int axis = splitOnXAxis ? 0 : 1;

            Span<int> span = _indices.AsSpan(startIndex, count);
            int leftCount = count / 2;
            if (leftCount == 0)
            {
                Bounds leafBounds = CalculateLeafBounds(startIndex, count);
                return KdTreeNode.CreateLeaf(leafBounds, startIndex, count);
            }

            SelectKth(span, leftCount, axis);
            int rightCount = count - leftCount;
            if (rightCount == 0)
            {
                Bounds leafBounds = CalculateLeafBounds(startIndex, count);
                return KdTreeNode.CreateLeaf(leafBounds, startIndex, count);
            }

            KdTreeNode left = BuildBalanced(startIndex, leftCount, depth + 1);
            KdTreeNode right = BuildBalanced(startIndex + leftCount, rightCount, depth + 1);
            Bounds boundary = CombineChildBounds(left.boundary, right.boundary);
            return KdTreeNode.CreateInternal(boundary, left, right, startIndex, count);
        }

        private KdTreeNode BuildUnbalanced(
            int startIndex,
            int count,
            bool splitOnXAxis,
            int[] scratch
        )
        {
            Span<int> source = _indices.AsSpan(startIndex, count);
            Span<int> temp = scratch.AsSpan(0, count);
            Entry[] entries = _entries;

            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;

            for (int i = 0; i < count; ++i)
            {
                Vector2 position = entries[source[i]].position;
                if (position.x < minX)
                {
                    minX = position.x;
                }
                if (position.y < minY)
                {
                    minY = position.y;
                }
                if (maxX < position.x)
                {
                    maxX = position.x;
                }
                if (maxY < position.y)
                {
                    maxY = position.y;
                }
            }

            Bounds nodeBounds = CreateBounds(minX, maxX, minY, maxY);

            if (count <= _bucketSize)
            {
                return KdTreeNode.CreateLeaf(nodeBounds, startIndex, count);
            }

            float cutoff = splitOnXAxis ? nodeBounds.center.x : nodeBounds.center.y;

            int leftWrite = 0;
            int rightWrite = count - 1;
            for (int i = 0; i < count; ++i)
            {
                int entryIndex = source[i];
                Vector2 position = entries[entryIndex].position;
                float value = splitOnXAxis ? position.x : position.y;
                if (value <= cutoff)
                {
                    temp[leftWrite++] = entryIndex;
                }
                else
                {
                    temp[rightWrite--] = entryIndex;
                }
            }

            int leftCount = leftWrite;
            int rightCount = count - leftCount;

            if (leftCount == 0 || rightCount == 0)
            {
                return KdTreeNode.CreateLeaf(nodeBounds, startIndex, count);
            }

            temp.CopyTo(source);

            KdTreeNode left = BuildUnbalanced(startIndex, leftCount, !splitOnXAxis, scratch);
            KdTreeNode right = BuildUnbalanced(
                startIndex + leftCount,
                rightCount,
                !splitOnXAxis,
                scratch
            );
            Bounds boundary = CombineChildBounds(left.boundary, right.boundary);
            return KdTreeNode.CreateInternal(boundary, left, right, startIndex, count);
        }

        private Bounds CalculateLeafBounds(int startIndex, int count)
        {
            if (count <= 0)
            {
                return new Bounds();
            }

            Entry[] entries = _entries;
            int[] indices = _indices;
            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;

            int end = startIndex + count;
            for (int i = startIndex; i < end; ++i)
            {
                Vector2 position = entries[indices[i]].position;
                if (position.x < minX)
                {
                    minX = position.x;
                }
                if (position.y < minY)
                {
                    minY = position.y;
                }
                if (maxX < position.x)
                {
                    maxX = position.x;
                }
                if (maxY < position.y)
                {
                    maxY = position.y;
                }
            }

            return CreateBounds(minX, maxX, minY, maxY);
        }

        private void SelectKth(Span<int> span, int k, int axis)
        {
            Entry[] entries = _entries;
            int left = 0;
            int right = span.Length - 1;

            while (left < right)
            {
                if (right - left <= SmallPartitionThreshold)
                {
                    InsertionSort(span.Slice(left, right - left + 1), axis, entries);
                    return;
                }

                int pivotIndex = SelectPivot(span, left, right, axis, entries);
                float pivot = GetAxis(entries[span[pivotIndex]], axis);

                int i = left;
                int j = right;

                if (axis == 0)
                {
                    while (i <= j)
                    {
                        while (i <= j && entries[span[i]].position.x < pivot)
                        {
                            i++;
                        }

                        while (i <= j && pivot < entries[span[j]].position.x)
                        {
                            j--;
                        }

                        if (i <= j)
                        {
                            (span[i], span[j]) = (span[j], span[i]);
                            i++;
                            j--;
                        }
                    }
                }
                else
                {
                    while (i <= j)
                    {
                        while (i <= j && entries[span[i]].position.y < pivot)
                        {
                            i++;
                        }

                        while (i <= j && pivot < entries[span[j]].position.y)
                        {
                            j--;
                        }

                        if (i <= j)
                        {
                            (span[i], span[j]) = (span[j], span[i]);
                            i++;
                            j--;
                        }
                    }
                }

                if (k <= j)
                {
                    right = j;
                    continue;
                }

                if (i <= k)
                {
                    left = i;
                    continue;
                }

                return;
            }
        }

        private static int SelectPivot(
            Span<int> span,
            int left,
            int right,
            int axis,
            Entry[] entries
        )
        {
            int mid = left + ((right - left) >> 1);

            float leftValue = GetAxis(entries[span[left]], axis);
            float midValue = GetAxis(entries[span[mid]], axis);
            float rightValue = GetAxis(entries[span[right]], axis);

            if (midValue < leftValue)
            {
                (span[left], span[mid]) = (span[mid], span[left]);
                (leftValue, midValue) = (midValue, leftValue);
            }

            if (rightValue < midValue)
            {
                (span[mid], span[right]) = (span[right], span[mid]);
                (midValue, rightValue) = (rightValue, midValue);

                if (midValue < leftValue)
                {
                    (span[left], span[mid]) = (span[mid], span[left]);
                    (leftValue, midValue) = (midValue, leftValue);
                }
            }

            return mid;
        }

        private static void InsertionSort(Span<int> span, int axis, Entry[] entries)
        {
            if (span.Length <= 1)
            {
                return;
            }

            if (axis == 0)
            {
                for (int i = 1; i < span.Length; ++i)
                {
                    int currentIndex = span[i];
                    float currentValue = entries[currentIndex].position.x;
                    int j = i - 1;

                    while (0 <= j && currentValue < entries[span[j]].position.x)
                    {
                        span[j + 1] = span[j];
                        j--;
                    }

                    span[j + 1] = currentIndex;
                }
            }
            else
            {
                for (int i = 1; i < span.Length; ++i)
                {
                    int currentIndex = span[i];
                    float currentValue = entries[currentIndex].position.y;
                    int j = i - 1;

                    while (0 <= j && currentValue < entries[span[j]].position.y)
                    {
                        span[j + 1] = span[j];
                        j--;
                    }

                    span[j + 1] = currentIndex;
                }
            }
        }

        private static float GetAxis(in Entry entry, int axis) =>
            axis == 0 ? entry.position.x : entry.position.y;

        private static Bounds CombineChildBounds(Bounds left, Bounds right)
        {
            Bounds combined = left;
            combined.Encapsulate(right);
            EnsureMinimumBounds(ref combined);
            return combined;
        }

        private static Bounds CreateBounds(float minX, float maxX, float minY, float maxY)
        {
            if (float.IsInfinity(minX) || float.IsInfinity(minY))
            {
                return new Bounds();
            }

            Vector3 min = new(minX, minY, 0f);
            Vector3 max = new(maxX, maxY, 0f);
            Vector3 center = (min + max) * 0.5f;
            Vector3 size = max - min;
            Bounds bounds = new(center, new Vector3(size.x, size.y, 1f));
            EnsureMinimumBounds(ref bounds);
            return bounds;
        }

        private static void EnsureMinimumBounds(ref Bounds bounds)
        {
            Vector3 size = bounds.size;
            if (size.x < MinimumNodeSize)
            {
                size.x = MinimumNodeSize;
            }
            if (size.y < MinimumNodeSize)
            {
                size.y = MinimumNodeSize;
            }
            size.z = 1f;
            bounds.size = size;
        }

        public List<T> GetElementsInRange(
            Vector2 position,
            float range,
            List<T> elementsInRange,
            float minimumRange = 0
        )
        {
            elementsInRange.Clear();
            // Allow zero range to return only exact matches (distance == 0)
            if (range < 0f || _head._count <= 0)
            {
                return elementsInRange;
            }

            Bounds bounds = new(position, new Vector3(range * 2f, range * 2f, 1f));

            if (!bounds.FastIntersects2D(_bounds))
            {
                return elementsInRange;
            }

            using PooledResource<Stack<KdTreeNode>> stackResource = Buffers<KdTreeNode>.Stack.Get(
                out Stack<KdTreeNode> nodesToVisit
            );
            nodesToVisit.Push(_head);

            Entry[] entries = _entries;
            int[] indices = _indices;
            float rangeSquared = range * range;
            bool hasMinimumRange = 0f < minimumRange;
            float minimumRangeSquared = minimumRange * minimumRange;

            while (nodesToVisit.TryPop(out KdTreeNode currentNode))
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

                KdTreeNode left = currentNode.left;
                if (left is not null && 0 < left._count && bounds.FastIntersects2D(left.boundary))
                {
                    nodesToVisit.Push(left);
                }

                KdTreeNode right = currentNode.right;
                if (
                    right is not null
                    && 0 < right._count
                    && bounds.FastIntersects2D(right.boundary)
                )
                {
                    nodesToVisit.Push(right);
                }
            }

            return elementsInRange;
        }

        public List<T> GetElementsInBounds(Bounds bounds, List<T> elementsInBounds)
        {
            elementsInBounds.Clear();
            if (_head._count <= 0 || !bounds.FastIntersects2D(_bounds))
            {
                return elementsInBounds;
            }
            using PooledResource<Stack<KdTreeNode>> stackResource = Buffers<KdTreeNode>.Stack.Get(
                out Stack<KdTreeNode> nodesToVisit
            );
            nodesToVisit.Push(_head);

            Entry[] entries = _entries;
            int[] indices = _indices;

            while (nodesToVisit.TryPop(out KdTreeNode currentNode))
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

                KdTreeNode left = currentNode.left;
                if (left is not null && 0 < left._count && bounds.FastIntersects2D(left.boundary))
                {
                    nodesToVisit.Push(left);
                }

                KdTreeNode right = currentNode.right;
                if (
                    right is not null
                    && 0 < right._count
                    && bounds.FastIntersects2D(right.boundary)
                )
                {
                    nodesToVisit.Push(right);
                }
            }

            return elementsInBounds;
        }

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

            using PooledResource<Stack<KdTreeNode>> nodeBufferResource =
                Buffers<KdTreeNode>.Stack.Get(out Stack<KdTreeNode> nodeBuffer);
            nodeBuffer.Push(_head);
            using PooledResource<HashSet<T>> nearestNeighborBufferResource = Buffers<T>.HashSet.Get(
                out HashSet<T> nearestNeighborBuffer
            );
            using PooledResource<List<Neighbor>> neighborCandidatesResource =
                Buffers<Neighbor>.List.Get(out List<Neighbor> neighborCandidates);

            Entry[] entries = _entries;
            int[] indices = _indices;

            KdTreeNode current = _head;

            while (!current.isTerminal)
            {
                KdTreeNode left = current.left;
                KdTreeNode right = current.right;
                if (left is null || left._count == 0)
                {
                    if (right is null || right._count == 0)
                    {
                        break;
                    }

                    nodeBuffer.Push(right);
                    current = right;
                    if (right._count <= count)
                    {
                        break;
                    }

                    continue;
                }

                if (right is null || right._count == 0)
                {
                    nodeBuffer.Push(left);
                    current = left;
                    if (left._count <= count)
                    {
                        break;
                    }

                    continue;
                }

                float leftDistance = ((Vector2)left.boundary.center - position).sqrMagnitude;
                float rightDistance = ((Vector2)right.boundary.center - position).sqrMagnitude;
                if (leftDistance < rightDistance)
                {
                    nodeBuffer.Push(left);
                    current = left;
                    if (left._count <= count)
                    {
                        break;
                    }
                }
                else
                {
                    nodeBuffer.Push(right);
                    current = right;
                    if (right._count <= count)
                    {
                        break;
                    }
                }
            }

            while (
                nearestNeighborBuffer.Count < count && nodeBuffer.TryPop(out KdTreeNode selected)
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

        private sealed class NeighborComparer : IComparer<Neighbor>
        {
            internal static readonly NeighborComparer Instance = new();

            public int Compare(Neighbor x, Neighbor y)
            {
                return x.sqrDistance.CompareTo(y.sqrDistance);
            }
        }

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
        public sealed class KdTreeNode
        {
            public readonly Bounds boundary;
            public readonly KdTreeNode left;
            public readonly KdTreeNode right;
            internal readonly int _startIndex;
            internal readonly int _count;
            public readonly bool isTerminal;

            private KdTreeNode(
                Bounds boundary,
                KdTreeNode left,
                KdTreeNode right,
                int startIndex,
                int count,
                bool isTerminal
            )
            {
                this.boundary = boundary;
                this.left = left;
                this.right = right;
                _startIndex = startIndex;
                _count = count;
                this.isTerminal = isTerminal;
            }

            internal static KdTreeNode CreateLeaf(Bounds boundary, int startIndex, int count)
            {
                return new KdTreeNode(boundary, null, null, startIndex, count, true);
            }

            internal static KdTreeNode CreateInternal(
                Bounds boundary,
                KdTreeNode left,
                KdTreeNode right,
                int startIndex,
                int count
            )
            {
                return new KdTreeNode(boundary, left, right, startIndex, count, false);
            }
        }
    }
}
