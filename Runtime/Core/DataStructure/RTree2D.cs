// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Runtime.CompilerServices;
    using Extension;
    using UnityEngine;
    using Utils;

    /// <summary>
    /// Immutable 2D R-tree for efficient spatial indexing of rectangular bounds.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// RTree2D<Collider>.Entry[] entries = colliders.Select(c => new RTree2D<Collider>.Entry(c, c.bounds)).ToArray();
    /// RTree2D<Collider> tree = RTree2D<Collider>.Build(entries);
    /// List<Collider> results = new List<Collider>();
    /// tree.GetElementsInBounds(searchBounds, results);
    /// ]]></code>
    /// </example>
    /// <typeparam name="T">Element type.</typeparam>
    /// <remarks>
    /// Pros: Great for sized objects (sprites, colliders) with area; supports fast rectangle and radius queries.
    /// Cons: Immutable; rebuild when element bounds change.
    /// Semantics: RTree2D indexes rectangles (AABBs) rather than points; as such its query results intentionally
    /// differ from point-based structures like QuadTree2D/KdTree2D for the same scene when elements have size.
    /// </remarks>
    [Serializable]
    public sealed class RTree2D<T> : ISpatialTree2D<T>
    {
        internal const float MinimumNodeSize = 0.001f;

        [Serializable]
        internal struct ElementData
        {
            internal T _value;
            internal Bounds _bounds;
            internal Vector2 _center;
            internal ulong _sortKey;
        }

        [Serializable]
        public sealed class RTreeNode
        {
            public readonly Bounds boundary;
            internal readonly RTreeNode[] _children;
            internal readonly int _startIndex;
            internal readonly int _count;
            public readonly bool isTerminal;

            private RTreeNode(int startIndex, int count, Bounds boundary, RTreeNode[] children)
            {
                _startIndex = startIndex;
                _count = count;
                this.boundary = boundary;
                _children = children ?? Array.Empty<RTreeNode>();
                isTerminal = _children.Length == 0;
            }

            internal static RTreeNode CreateEmpty()
            {
                return new RTreeNode(0, 0, new Bounds(), Array.Empty<RTreeNode>());
            }

            internal static RTreeNode CreateLeaf(ElementData[] elements, int startIndex, int count)
            {
                Bounds nodeBounds = CalculateBounds(elements, startIndex, count);
                return new RTreeNode(startIndex, count, nodeBounds, Array.Empty<RTreeNode>());
            }

            internal static RTreeNode CreateInternal(RTreeNode[] children)
            {
                if (children.Length == 0)
                {
                    return CreateEmpty();
                }

                int startIndex = children[0]._startIndex;
                int lastChildIndex = children.Length - 1;
                RTreeNode lastChild = children[lastChildIndex];
                int endIndex = lastChild._startIndex + lastChild._count;
                Bounds nodeBounds = children[0].boundary;
                for (int i = 1; i < children.Length; ++i)
                {
                    nodeBounds.Encapsulate(children[i].boundary);
                }

                nodeBounds = EnsureMinimumBounds(nodeBounds);
                return new RTreeNode(startIndex, endIndex - startIndex, nodeBounds, children);
            }
        }

        /// <summary>
        /// Default number of elements per leaf node.
        /// </summary>
        public const int DefaultBucketSize = 10;
        public const int DefaultBranchFactor = 4;

        public readonly ImmutableArray<T> elements;

        /// <summary>
        /// Gets the overall bounding box of the tree.
        /// </summary>
        public Bounds Boundary => _bounds;

        private readonly Bounds _bounds;
        private readonly ElementData[] _elementData;
        private readonly RTreeNode _head;

        /// <summary>
        /// Builds an R-Tree from elements using a transformer that returns each element's bounds.
        /// </summary>
        /// <param name="points">Source elements.</param>
        /// <param name="elementTransformer">Maps element to an axis-aligned bounding box in world space.</param>
        /// <param name="bucketSize">Max elements per leaf.</param>
        /// <param name="branchFactor">Approximate number of children per internal node (≥2).</param>
        /// <exception cref="ArgumentNullException">Thrown when points or elementTransformer are null.</exception>
        public RTree2D(
            IEnumerable<T> points,
            Func<T, Bounds> elementTransformer,
            int bucketSize = DefaultBucketSize,
            int branchFactor = DefaultBranchFactor
        )
        {
            elements =
                points?.ToImmutableArray() ?? throw new ArgumentNullException(nameof(points));

            Func<T, Bounds> transformer =
                elementTransformer ?? throw new ArgumentNullException(nameof(elementTransformer));

            int elementCount = elements.Length;
            _elementData = new ElementData[elementCount];
            ElementData[] elementData = _elementData;
            bucketSize = Math.Max(1, bucketSize);
            branchFactor = Math.Max(2, branchFactor);
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            bool hasElements = false;

            for (int i = 0; i < elementCount; ++i)
            {
                T element = elements[i];

                Bounds elementBounds = transformer(element);
                ElementData data = default;
                data._value = element;
                data._bounds = elementBounds;
                data._center = elementBounds.center;
                elementData[i] = data;
                Vector3 min = elementBounds.min;
                Vector3 max = elementBounds.max;

                if (!hasElements)
                {
                    hasElements = true;
                }

                if (min.x < minX)
                {
                    minX = min.x;
                }
                if (min.y < minY)
                {
                    minY = min.y;
                }
                if (maxX < max.x)
                {
                    maxX = max.x;
                }
                if (maxY < max.y)
                {
                    maxY = max.y;
                }
            }

            Bounds bounds = hasElements
                ? new Bounds(
                    new Vector3(minX + (maxX - minX) / 2, minY + (maxY - minY) / 2, 0f),
                    new Vector3(maxX - minX, maxY - minY, 0f)
                )
                : new Bounds();

            // Ensure bounds have minimum size to handle colinear points
            // FastContains2D uses strict < for max bounds, so zero-size dimensions won't contain any points
            if (hasElements)
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
                bounds.size = size;
            }

            _bounds = bounds;
            if (!hasElements)
            {
                _head = RTreeNode.CreateEmpty();
                return;
            }

            float rangeX = maxX - minX;
            float rangeY = maxY - minY;
            float inverseRangeX = float.Epsilon < rangeX ? 1f / rangeX : 0f;
            float inverseRangeY = float.Epsilon < rangeY ? 1f / rangeY : 0f;

            if (0 < elementCount)
            {
                ref ElementData elementRef = ref elementData[0];
                for (int i = 0; i < elementCount; ++i)
                {
                    ref ElementData data = ref Unsafe.Add(ref elementRef, i);
                    Vector2 center = data._center;
                    float normalizedX = (center.x - minX) * inverseRangeX;
                    float normalizedY = (center.y - minY) * inverseRangeY;
                    ushort quantizedX = QuantizeNormalized(normalizedX);
                    ushort quantizedY = QuantizeNormalized(normalizedY);
                    uint mortonKey = EncodeMorton(quantizedX, quantizedY);
                    data._sortKey = ComposeSortKey(mortonKey, quantizedX, quantizedY);
                }
            }

            if (1 < elementCount)
            {
                RadixSort(elementData, elementCount);
            }

            using PooledResource<List<RTreeNode>> nodeBufferResource = Buffers<RTreeNode>.List.Get(
                out List<RTreeNode> currentLevel
            );
            for (int startIndex = 0; startIndex < elementCount; startIndex += bucketSize)
            {
                int count = Math.Min(bucketSize, elementCount - startIndex);
                currentLevel.Add(RTreeNode.CreateLeaf(elementData, startIndex, count));
            }

            while (1 < currentLevel.Count)
            {
                using PooledResource<List<RTreeNode>> nextLevelResource =
                    Buffers<RTreeNode>.List.Get(out List<RTreeNode> nextLevel);
                for (int i = 0; i < currentLevel.Count; i += branchFactor)
                {
                    int childCount = Math.Min(branchFactor, currentLevel.Count - i);
                    RTreeNode[] children = new RTreeNode[childCount];
                    currentLevel.CopyTo(i, children, 0, childCount);
                    nextLevel.Add(RTreeNode.CreateInternal(children));
                }

                currentLevel.Clear();
                currentLevel.AddRange(nextLevel);
            }

            _head = currentLevel[0];
            _bounds = _head.boundary;
        }

        private void CollectElementIndicesInBounds(Bounds bounds, List<int> indices)
        {
            indices.Clear();
            if (!bounds.FastIntersects2D(_bounds))
            {
                return;
            }

            using PooledResource<Stack<RTreeNode>> nodeBufferResource =
                Buffers<RTreeNode>.Stack.Get(out Stack<RTreeNode> nodesToVisit);
            nodesToVisit.Push(_head);

            while (nodesToVisit.TryPop(out RTreeNode currentNode))
            {
                if (!bounds.FastIntersects2D(currentNode.boundary))
                {
                    continue;
                }

                if (currentNode.isTerminal)
                {
                    int start = currentNode._startIndex;
                    int end = start + currentNode._count;
                    for (int i = start; i < end; ++i)
                    {
                        ElementData elementData = _elementData[i];
                        if (bounds.FastIntersects2D(elementData._bounds))
                        {
                            indices.Add(i);
                        }
                    }

                    continue;
                }

                RTreeNode[] childNodes = currentNode._children;
                foreach (RTreeNode child in childNodes)
                {
                    if (child._count <= 0)
                    {
                        continue;
                    }

                    if (!bounds.FastIntersects2D(child.boundary))
                    {
                        continue;
                    }

                    nodesToVisit.Push(child);
                }
            }
        }

        /// <summary>
        /// Finds all elements within distance <paramref name="range"/> of <paramref name="position"/> (circle query).
        /// </summary>
        /// <param name="position">Query center.</param>
        /// <param name="range">Query radius.</param>
        /// <param name="elementsInRange">Destination list cleared before use.</param>
        /// <param name="minimumRange">Optional inner exclusion radius.</param>
        /// <returns>The destination list, for chaining.</returns>
        public List<T> GetElementsInRange(
            Vector2 position,
            float range,
            List<T> elementsInRange,
            float minimumRange = 0f
        )
        {
            elementsInRange.Clear();
            if (range <= 0f)
            {
                return elementsInRange;
            }

            Bounds queryBounds = new(
                new Vector3(position.x, position.y, 0f),
                new Vector3(range * 2f, range * 2f, 1f)
            );

            if (!queryBounds.FastIntersects2D(_bounds))
            {
                return elementsInRange;
            }

            using PooledResource<List<int>> candidateIndicesResource = Buffers<int>.List.Get(
                out List<int> candidateIndices
            );
            CollectElementIndicesInBounds(queryBounds, candidateIndices);
            if (candidateIndices.Count == 0)
            {
                return elementsInRange;
            }

            Circle area = new(position, range);
            bool hasMinimumRange = 0f < minimumRange;
            Circle minimumArea = default;
            if (hasMinimumRange)
            {
                minimumArea = new Circle(position, minimumRange);
            }

            foreach (int index in candidateIndices)
            {
                ElementData elementData = _elementData[index];
                Bounds elementBoundary = elementData._bounds;
                if (!area.Intersects(elementBoundary))
                {
                    continue;
                }

                if (hasMinimumRange && minimumArea.Intersects(elementBoundary))
                {
                    continue;
                }

                elementsInRange.Add(elementData._value);
            }

            return elementsInRange;
        }

        /// <summary>
        /// Finds all elements whose bounds intersect the specified axis-aligned box.
        /// </summary>
        /// <returns>The destination list, for chaining.</returns>
        public List<T> GetElementsInBounds(Bounds bounds, List<T> elementsInBounds)
        {
            elementsInBounds.Clear();
            if (!bounds.FastIntersects2D(_bounds))
            {
                return elementsInBounds;
            }

            using PooledResource<List<int>> indicesResource = Buffers<int>.List.Get(
                out List<int> indices
            );
            CollectElementIndicesInBounds(bounds, indices);
            foreach (int index in indices)
            {
                elementsInBounds.Add(_elementData[index]._value);
            }

            return elementsInBounds;
        }

        /// <summary>
        /// Returns an approximate set of the nearest <paramref name="count"/> neighbors to <paramref name="position"/>.
        /// </summary>
        /// <remarks>
        /// Heavily adapted from ANN strategies for speed; suitable for gameplay proximity needs.
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

            using PooledResource<Stack<RTreeNode>> nodeBufferResource =
                Buffers<RTreeNode>.Stack.Get(out Stack<RTreeNode> stack);
            stack.Push(_head);

            using PooledResource<List<RTreeNode>> childrenBufferResource =
                Buffers<RTreeNode>.List.Get(out List<RTreeNode> childrenCopy);
            using PooledResource<HashSet<T>> nearestNeighborBufferResource = Buffers<T>.HashSet.Get(
                out HashSet<T> nearestNeighborsSet
            );
            using PooledResource<List<int>> nearestIndexBufferResource = Buffers<int>.List.Get(
                out List<int> nearestIndices
            );

            ElementData[] elementData = _elementData;

            RTreeNode current = _head;

            while (!current.isTerminal)
            {
                childrenCopy.Clear();
                RTreeNode[] childNodes = current._children;
                for (int i = 0; i < childNodes.Length; ++i)
                {
                    RTreeNode child = childNodes[i];
                    if (child is not null && 0 < child._count)
                    {
                        childrenCopy.Add(child);
                    }
                }

                if (childrenCopy.Count == 0)
                {
                    break;
                }

                SortChildrenByDistance(childrenCopy, position);
                for (int i = childrenCopy.Count - 1; 0 <= i; --i)
                {
                    stack.Push(childrenCopy[i]);
                }

                current = childrenCopy[0];
                if (current._count <= count)
                {
                    break;
                }
            }

            while (nearestNeighborsSet.Count < count && stack.TryPop(out RTreeNode selected))
            {
                int startIndex = selected._startIndex;
                int endIndex = startIndex + selected._count;
                for (int i = startIndex; i < endIndex; ++i)
                {
                    ElementData data = elementData[i];
                    if (!nearestNeighborsSet.Add(data._value))
                    {
                        continue;
                    }

                    nearestIndices.Add(i);
                    if (count <= nearestNeighborsSet.Count)
                    {
                        break;
                    }
                }
            }

            if (nearestIndices.Count == 0)
            {
                return nearestNeighbors;
            }

            if (count < nearestIndices.Count)
            {
                SortIndicesByDistance(nearestIndices, elementData, position);
                nearestIndices.RemoveRange(count, nearestIndices.Count - count);
            }

            foreach (int index in nearestIndices)
            {
                nearestNeighbors.Add(elementData[index]._value);
            }

            return nearestNeighbors;
        }

        private static void SortChildrenByDistance(List<RTreeNode> nodes, Vector2 searchPosition)
        {
            for (int i = 1; i < nodes.Count; ++i)
            {
                RTreeNode value = nodes[i];
                float valueDistance = GetNodeSqrDistance(value, searchPosition);
                int j = i - 1;
                while (0 <= j)
                {
                    RTreeNode previous = nodes[j];
                    if (GetNodeSqrDistance(previous, searchPosition) <= valueDistance)
                    {
                        break;
                    }

                    nodes[j + 1] = previous;
                    --j;
                }

                nodes[j + 1] = value;
            }
        }

        private static float GetNodeSqrDistance(RTreeNode node, Vector2 position)
        {
            return ((Vector2)node.boundary.center - position).sqrMagnitude;
        }

        private static void SortIndicesByDistance(
            List<int> indices,
            ElementData[] elements,
            Vector2 position
        )
        {
            for (int i = 1; i < indices.Count; ++i)
            {
                int currentIndex = indices[i];
                float currentDistance = GetElementSqrDistance(elements[currentIndex], position);
                int j = i - 1;
                while (0 <= j)
                {
                    int previousIndex = indices[j];
                    if (GetElementSqrDistance(elements[previousIndex], position) <= currentDistance)
                    {
                        break;
                    }

                    indices[j + 1] = previousIndex;
                    --j;
                }

                indices[j + 1] = currentIndex;
            }
        }

        private static float GetElementSqrDistance(ElementData element, Vector2 position)
        {
            return (element._center - position).sqrMagnitude;
        }

        private static void RadixSort(ElementData[] elements, int length)
        {
            if (length <= 1)
            {
                return;
            }

            const int BitsPerPass = 8;
            const int BucketCount = 1 << BitsPerPass;
            Span<int> counts = stackalloc int[BucketCount];

            using PooledArray<ElementData> scratchResource = SystemArrayPool<ElementData>.Get(
                length,
                out ElementData[] scratch
            );
            ElementData[] source = elements;
            ElementData[] destination = scratch;
            bool dataInScratch = false;

            for (int shift = 0; shift < 64; shift += BitsPerPass)
            {
                counts.Clear();
                ref ElementData sourceRef = ref source[0];
                for (int i = 0; i < length; ++i)
                {
                    ulong key = Unsafe.Add(ref sourceRef, i)._sortKey;
                    counts[(int)((key >> shift) & (BucketCount - 1))]++;
                }

                int total = 0;
                for (int bucket = 0; bucket < BucketCount; ++bucket)
                {
                    int count = counts[bucket];
                    counts[bucket] = total;
                    total += count;
                }

                ref ElementData destinationRef = ref destination[0];
                for (int i = 0; i < length; ++i)
                {
                    ElementData value = Unsafe.Add(ref sourceRef, i);
                    int bucket = (int)((value._sortKey >> shift) & (BucketCount - 1));
                    Unsafe.Add(ref destinationRef, counts[bucket]++) = value;
                }

                (source, destination) = (destination, source);
                dataInScratch = !dataInScratch;
            }

            if (dataInScratch)
            {
                Array.Copy(source, elements, length);
            }
        }

        private static Bounds CalculateBounds(ElementData[] elements, int startIndex, int count)
        {
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            int endIndex = startIndex + count;
            for (int i = startIndex; i < endIndex; ++i)
            {
                Bounds bounds = elements[i]._bounds;
                Vector3 min = bounds.min;
                Vector3 max = bounds.max;
                minX = Math.Min(minX, min.x);
                maxX = Math.Max(maxX, max.x);
                minY = Math.Min(minY, min.y);
                maxY = Math.Max(maxY, max.y);
            }

            Bounds nodeBounds = new(
                new Vector3(minX + (maxX - minX) / 2f, minY + (maxY - minY) / 2f, 0f),
                new Vector3(maxX - minX, maxY - minY, 0f)
            );

            return EnsureMinimumBounds(nodeBounds);
        }

        private static Bounds EnsureMinimumBounds(Bounds bounds)
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

            bounds.size = size;
            return bounds;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint EncodeMorton(ushort quantizedX, ushort quantizedY)
        {
            uint mortonX = Part1By1(quantizedX);
            uint mortonY = Part1By1(quantizedY);
            return mortonX | (mortonY << 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ushort QuantizeNormalized(float normalized)
        {
            if (normalized <= 0f)
            {
                return 0;
            }

            if (1f <= normalized)
            {
                return 65535;
            }

            return (ushort)(normalized * 65535f + 0.5f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong ComposeSortKey(uint mortonKey, ushort quantizedX, ushort quantizedY)
        {
            return ((ulong)mortonKey << 32) | ((ulong)quantizedX << 16) | quantizedY;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Part1By1(uint value)
        {
            value &= 0x0000ffff;
            value = (value | (value << 8)) & 0x00FF00FF;
            value = (value | (value << 4)) & 0x0F0F0F0F;
            value = (value | (value << 2)) & 0x33333333;
            value = (value | (value << 1)) & 0x55555555;
            return value;
        }
    }
}
