// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Runtime.CompilerServices;
    using UnityEngine;
    using Utils;

    /// <summary>
    /// Immutable 3D k-d tree for efficient nearest neighbor, range, and bounds queries in 3D space.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// List<Vector3> points = SamplePoints();
    /// KdTree3D<Vector3> tree = new KdTree3D<Vector3>(points, p => p);
    /// List<Vector3> neighbors = new List<Vector3>();
    /// tree.GetElementsInRange(queryPosition, 4f, neighbors);
    /// ]]></code>
    /// </example>
    /// <typeparam name="T">Element type contained in the tree.</typeparam>
    /// <remarks>
    /// <para>Pros: Very fast nearest neighbor performance; good for static or batched updates.</para>
    /// <para>Cons: Immutable structure by design; rebuild when positions change frequently.</para>
    /// <para>Semantics: Due to algorithmic choices (axis-aligned splitting, half-open containment checks,
    /// minimum node-size enforcement, and tie-handling on split planes), KdTree3D (balanced and unbalanced)
    /// may return different edge-case results compared to OctTree3D for identical inputs/queries—especially for
    /// points lying exactly on query boundaries or split planes. See docs/features/spatial/spatial-tree-semantics.md for details.</para>
    /// </remarks>
    [Serializable]
    public sealed class KdTree3D<T> : ISpatialTree3D<T>
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
        private readonly float[] _positionsX;
        private readonly float[] _positionsY;
        private readonly float[] _positionsZ;
        private readonly int[] _indices;
        private readonly KdTreeNode _head;
        private readonly bool _balanced;
        private readonly int _bucketSize;

        /// <summary>
        /// Builds a 3D k-d tree from elements using a transformer to extract 3D positions.
        /// </summary>
        /// <param name="points">Source elements.</param>
        /// <param name="elementTransformer">Maps element to its 3D position.</param>
        /// <param name="bucketSize">Max elements per leaf. Minimum 1.</param>
        /// <param name="balanced">If true, builds a balanced tree by median selection; otherwise uses a quick split strategy.</param>
        /// <exception cref="ArgumentNullException">Thrown when points or elementTransformer are null.</exception>
        public KdTree3D(
            IEnumerable<T> points,
            Func<T, Vector3> elementTransformer,
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
            _positionsX = elementCount == 0 ? Array.Empty<float>() : new float[elementCount];
            _positionsY = elementCount == 0 ? Array.Empty<float>() : new float[elementCount];
            _positionsZ = elementCount == 0 ? Array.Empty<float>() : new float[elementCount];
            _indices = elementCount == 0 ? Array.Empty<int>() : new int[elementCount];
            _balanced = balanced;
            _bucketSize = Math.Max(1, bucketSize);

            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float minZ = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            float maxZ = float.NegativeInfinity;

            for (int i = 0; i < elementCount; ++i)
            {
                T element = elements[i];
                Vector3 position = elementTransformer(element);
                _positionsX[i] = position.x;
                _positionsY[i] = position.y;
                _positionsZ[i] = position.z;

                if (position.x < minX)
                {
                    minX = position.x;
                }
                if (position.y < minY)
                {
                    minY = position.y;
                }
                if (position.z < minZ)
                {
                    minZ = position.z;
                }
                if (maxX < position.x)
                {
                    maxX = position.x;
                }
                if (maxY < position.y)
                {
                    maxY = position.y;
                }
                if (maxZ < position.z)
                {
                    maxZ = position.z;
                }

                _indices[i] = i;
            }

            Bounds bounds = CreateBounds(minX, maxX, minY, maxY, minZ, maxZ);

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
                root = BuildUnbalanced(0, elementCount, axis: 0, scratch);
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

            int axis = depth % 3;

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

        private KdTreeNode BuildUnbalanced(int startIndex, int count, int axis, int[] scratch)
        {
            Span<int> source = _indices.AsSpan(startIndex, count);
            Span<int> temp = scratch.AsSpan(0, count);
            float[] positionsX = _positionsX;
            float[] positionsY = _positionsY;
            float[] positionsZ = _positionsZ;

            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float minZ = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            float maxZ = float.NegativeInfinity;

            for (int i = 0; i < count; ++i)
            {
                int entryIndex = source[i];
                float px = positionsX[entryIndex];
                float py = positionsY[entryIndex];
                float pz = positionsZ[entryIndex];
                if (px < minX)
                {
                    minX = px;
                }
                if (py < minY)
                {
                    minY = py;
                }
                if (pz < minZ)
                {
                    minZ = pz;
                }
                if (maxX < px)
                {
                    maxX = px;
                }
                if (maxY < py)
                {
                    maxY = py;
                }
                if (maxZ < pz)
                {
                    maxZ = pz;
                }
            }

            Bounds nodeBounds = CreateBounds(minX, maxX, minY, maxY, minZ, maxZ);

            if (count <= _bucketSize)
            {
                return KdTreeNode.CreateLeaf(nodeBounds, startIndex, count);
            }

            float cutoff = GetAxisValue(nodeBounds.center, axis);

            int leftWrite = 0;
            int rightWrite = count - 1;
            float[] axisArray = GetAxisArray(axis);
            for (int i = 0; i < count; ++i)
            {
                int entryIndex = source[i];
                float value = axisArray[entryIndex];
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

            int nextAxis = (axis + 1) % 3;
            KdTreeNode left = BuildUnbalanced(startIndex, leftCount, nextAxis, scratch);
            KdTreeNode right = BuildUnbalanced(
                startIndex + leftCount,
                rightCount,
                nextAxis,
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

            int[] indices = _indices;
            float[] positionsX = _positionsX;
            float[] positionsY = _positionsY;
            float[] positionsZ = _positionsZ;
            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float minZ = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            float maxZ = float.NegativeInfinity;

            int end = startIndex + count;
            for (int i = startIndex; i < end; ++i)
            {
                int entryIndex = indices[i];
                float px = positionsX[entryIndex];
                float py = positionsY[entryIndex];
                float pz = positionsZ[entryIndex];
                if (px < minX)
                {
                    minX = px;
                }
                if (py < minY)
                {
                    minY = py;
                }
                if (pz < minZ)
                {
                    minZ = pz;
                }
                if (maxX < px)
                {
                    maxX = px;
                }
                if (maxY < py)
                {
                    maxY = py;
                }
                if (maxZ < pz)
                {
                    maxZ = pz;
                }
            }

            return CreateBounds(minX, maxX, minY, maxY, minZ, maxZ);
        }

        private void SelectKth(Span<int> span, int k, int axis)
        {
            float[] axisValues = GetAxisArray(axis);
            int left = 0;
            int right = span.Length - 1;

            while (left < right)
            {
                if (right - left <= SmallPartitionThreshold)
                {
                    InsertionSort(span.Slice(left, right - left + 1), axisValues);
                    return;
                }

                int pivotIndex = SelectPivot(span, left, right, axisValues);
                float pivot = axisValues[span[pivotIndex]];

                int i = left;
                int j = right;

                while (i <= j)
                {
                    while (i <= j && axisValues[span[i]] < pivot)
                    {
                        i++;
                    }

                    while (i <= j && pivot < axisValues[span[j]])
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

        private static int SelectPivot(Span<int> span, int left, int right, float[] axisValues)
        {
            int mid = left + ((right - left) >> 1);

            float leftValue = axisValues[span[left]];
            float midValue = axisValues[span[mid]];
            float rightValue = axisValues[span[right]];

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

        private static void InsertionSort(Span<int> span, float[] axisValues)
        {
            if (span.Length <= 1)
            {
                return;
            }

            for (int i = 1; i < span.Length; ++i)
            {
                int currentIndex = span[i];
                float currentValue = axisValues[currentIndex];
                int j = i - 1;

                while (0 <= j && currentValue < axisValues[span[j]])
                {
                    span[j + 1] = span[j];
                    j--;
                }

                span[j + 1] = currentIndex;
            }
        }

        private static float GetAxisValue(Vector3 position, int axis)
        {
            return axis switch
            {
                0 => position.x,
                1 => position.y,
                _ => position.z,
            };
        }

        private static Bounds CombineChildBounds(Bounds left, Bounds right)
        {
            Bounds combined = left;
            combined.Encapsulate(right);
            EnsureMinimumBounds(ref combined);
            return combined;
        }

        private static Bounds CreateBounds(
            float minX,
            float maxX,
            float minY,
            float maxY,
            float minZ,
            float maxZ
        )
        {
            if (float.IsInfinity(minX) || float.IsInfinity(minY) || float.IsInfinity(minZ))
            {
                return new Bounds();
            }

            Vector3 min = new(minX, minY, minZ);
            Vector3 max = new(maxX, maxY, maxZ);
            Vector3 center = (min + max) * 0.5f;
            Vector3 size = max - min;
            Bounds bounds = new(center, size);
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
            if (size.z < MinimumNodeSize)
            {
                size.z = MinimumNodeSize;
            }
            bounds.size = size;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float[] GetAxisArray(int axis)
        {
            return axis switch
            {
                0 => _positionsX,
                1 => _positionsY,
                _ => _positionsZ,
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Vector3 GetPosition(int index)
        {
            return new Vector3(_positionsX[index], _positionsY[index], _positionsZ[index]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetDistanceSquared(int index, Vector3 point)
        {
            float dx = _positionsX[index] - point.x;
            float dy = _positionsY[index] - point.y;
            float dz = _positionsZ[index] - point.z;
            return dx * dx + dy * dy + dz * dz;
        }

        public List<T> GetElementsInRange(
            Vector3 position,
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

            Sphere querySphere = new(position, range);
            Bounds bounds = new(position, new Vector3(range * 2f, range * 2f, range * 2f));

            if (!bounds.Intersects(_bounds))
            {
                return elementsInRange;
            }

            using PooledResource<Stack<KdTreeNode>> stackResource = Buffers<KdTreeNode>.Stack.Get(
                out Stack<KdTreeNode> nodesToVisit
            );
            nodesToVisit.Push(_head);

            ImmutableArray<T> values = elements;
            int[] indices = _indices;
            float rangeSquared = range * range;
            bool hasMinimumRange = 0f < minimumRange;
            float minimumRangeSquared = minimumRange * minimumRange;
            Sphere minimumSphere = hasMinimumRange ? new Sphere(position, minimumRange) : default;

            while (nodesToVisit.TryPop(out KdTreeNode currentNode))
            {
                if (currentNode is null || currentNode._count <= 0)
                {
                    continue;
                }

                if (!bounds.Intersects(currentNode.boundary))
                {
                    continue;
                }

                // Use Sphere.Overlaps to check if the sphere fully contains the node's boundary
                BoundingBox3D nodeBoundary = BoundingBox3D.FromClosedBounds(currentNode.boundary);
                bool nodeFullyContained = querySphere.Overlaps(nodeBoundary);

                if (currentNode.isTerminal || nodeFullyContained)
                {
                    int start = currentNode._startIndex;
                    int end = start + currentNode._count;

                    // If the node is fully contained, we can skip distance checks for points
                    if (nodeFullyContained)
                    {
                        if (!hasMinimumRange)
                        {
                            // Fast path: all points in this node are within range
                            for (int i = start; i < end; ++i)
                            {
                                elementsInRange.Add(values[indices[i]]);
                            }
                        }
                        else
                        {
                            // Node is fully in outer sphere, but need to check minimum range
                            // Check if node is fully outside minimum sphere
                            bool nodeFullyOutsideMinimum = !minimumSphere.Intersects(nodeBoundary);
                            if (nodeFullyOutsideMinimum)
                            {
                                // Fast path: all points are in the annulus
                                for (int i = start; i < end; ++i)
                                {
                                    elementsInRange.Add(values[indices[i]]);
                                }
                            }
                            else
                            {
                                // Need to check each point against minimum range
                                for (int i = start; i < end; ++i)
                                {
                                    int elementIndex = indices[i];
                                    float squareDistance = GetDistanceSquared(
                                        elementIndex,
                                        position
                                    );
                                    if (minimumRangeSquared < squareDistance)
                                    {
                                        elementsInRange.Add(values[elementIndex]);
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        // Terminal node but not fully contained: check each point
                        for (int i = start; i < end; ++i)
                        {
                            int elementIndex = indices[i];
                            float squareDistance = GetDistanceSquared(elementIndex, position);
                            if (rangeSquared < squareDistance)
                            {
                                continue;
                            }

                            if (hasMinimumRange && squareDistance <= minimumRangeSquared)
                            {
                                continue;
                            }

                            elementsInRange.Add(values[elementIndex]);
                        }
                    }

                    continue;
                }

                KdTreeNode left = currentNode.left;
                if (left is not null && 0 < left._count && bounds.Intersects(left.boundary))
                {
                    nodesToVisit.Push(left);
                }

                KdTreeNode right = currentNode.right;
                if (right is not null && 0 < right._count && bounds.Intersects(right.boundary))
                {
                    nodesToVisit.Push(right);
                }
            }

            return elementsInRange;
        }

        public List<T> GetElementsInBounds(Bounds bounds, List<T> elementsInBounds)
        {
            elementsInBounds.Clear();
            if (_head._count <= 0)
            {
                return elementsInBounds;
            }

            // Use closed Unity Bounds intersection for traversal to avoid pruning
            // legitimate edge cases; final per-point checks use closed semantics.
            if (!bounds.Intersects(_bounds))
            {
                return elementsInBounds;
            }

            // Build inclusive half-open query box for robust per-point checks
            BoundingBox3D queryBox = BoundingBox3D.FromClosedBoundsInclusiveMax(bounds);

            using PooledResource<Stack<KdTreeNode>> stackResource = Buffers<KdTreeNode>.Stack.Get(
                out Stack<KdTreeNode> nodesToVisit
            );
            nodesToVisit.Push(_head);

            ImmutableArray<T> values = elements;
            int[] indices = _indices;

            while (nodesToVisit.TryPop(out KdTreeNode currentNode))
            {
                if (currentNode is null || currentNode._count <= 0)
                {
                    continue;
                }

                if (currentNode.isTerminal)
                {
                    int start = currentNode._startIndex;
                    int end = start + currentNode._count;
                    for (int i = start; i < end; ++i)
                    {
                        int elementIndex = indices[i];
                        Vector3 entryPosition = GetPosition(elementIndex);
                        // Use inclusive half-open check for robust closed semantics
                        if (queryBox.Contains(entryPosition))
                        {
                            elementsInBounds.Add(values[elementIndex]);
                        }
                    }

                    continue;
                }

                // Once we've reached an internal node that intersects the query,
                // visit both non-empty children and rely on per-point checks.
                KdTreeNode left = currentNode.left;
                if (left is not null && 0 < left._count)
                {
                    nodesToVisit.Push(left);
                }

                KdTreeNode right = currentNode.right;
                if (right is not null && 0 < right._count)
                {
                    nodesToVisit.Push(right);
                }
            }

            return elementsInBounds;
        }

        public List<T> GetApproximateNearestNeighbors(
            Vector3 position,
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

            ImmutableArray<T> values = elements;
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

                float leftDistance = (left.boundary.center - position).sqrMagnitude;
                float rightDistance = (right.boundary.center - position).sqrMagnitude;
                if (leftDistance < rightDistance)
                {
                    if (0 < right._count)
                    {
                        nodeBuffer.Push(right);
                    }

                    nodeBuffer.Push(left);
                    current = left;
                    if (left._count <= count)
                    {
                        break;
                    }
                }
                else
                {
                    if (0 < left._count)
                    {
                        nodeBuffer.Push(left);
                    }

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
                    int elementIndex = indices[i];
                    T value = values[elementIndex];
                    if (!nearestNeighborBuffer.Add(value))
                    {
                        continue;
                    }

                    float sqrDistance = GetDistanceSquared(elementIndex, position);
                    neighborCandidates.Add(new Neighbor(elementIndex, sqrDistance));
                }
            }

            if (1 < neighborCandidates.Count)
            {
                neighborCandidates.Sort(NeighborComparer.Instance);
            }

            nearestNeighbors.Clear();
            for (int i = 0; i < neighborCandidates.Count && i < count; ++i)
            {
                nearestNeighbors.Add(values[neighborCandidates[i].index]);
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

        private readonly struct Neighbor
        {
            public readonly int index;
            public readonly float sqrDistance;

            public Neighbor(int index, float sqrDistance)
            {
                this.index = index;
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
