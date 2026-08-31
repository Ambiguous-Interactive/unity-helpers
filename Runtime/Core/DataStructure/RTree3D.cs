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
    /// Immutable 3D R-tree for efficient spatial indexing of 3D bounds.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// RTree3D<Volume>.Entry[] entries = volumes.Select(v => new RTree3D<Volume>.Entry(v, v.Bounds)).ToArray();
    /// RTree3D<Volume> tree = RTree3D<Volume>.Build(entries);
    /// List<Volume> overlaps = new List<Volume>();
    /// tree.GetElementsInRange(origin, 8f, overlaps);
    /// ]]></code>
    /// </example>
    /// <typeparam name="T">Element type.</typeparam>
    /// <remarks>
    /// <para>Pros: Great for sized 3D objects (meshes, volumes) with fast box and radius intersection queries.</para>
    /// <para>Cons: Immutable; rebuild when element bounds change.</para>
    /// <para>Semantics: RTree3D indexes 3D bounds (AABBs), not points, and aggregates at node level using bounding volumes.
    /// As such, results differ by design from point-based structures like KdTree3D/OctTree3D for the same scene.</para>
    /// <para><b>A null destination throws <see cref="System.ArgumentNullException"/>.</b> That is a
    /// bug in the calling code rather than data the caller was handed, and the alternative is a bare
    /// <see cref="System.NullReferenceException"/> raised from inside the traversal, naming nothing.
    /// Do not "fix" it into a silent return.</para>
    /// </remarks>
    [Serializable]
    public sealed class RTree3D<T> : ISpatialTree3D<T>
    {
        internal const float MinimumNodeSize = 0.001f;

        /// <summary>Default number of elements per leaf node.</summary>
        public const int DefaultBucketSize = 10;
        public const int DefaultBranchFactor = 4;

        public readonly ImmutableArray<T> elements;

        /// <summary>
        /// Gets the overall bounding box of the tree (as Unity Bounds).
        /// </summary>
        public Bounds Boundary => _bounds.ToBounds();

        private readonly BoundingBox3D _bounds;
        private readonly ElementData[] _elementData;
        private readonly RTreeNode _head;

        /// <summary>
        /// Builds an R-Tree from elements using a transformer that returns each element's 3D bounds.
        /// </summary>
        /// <param name="points">Source elements.</param>
        /// <param name="elementTransformer">Maps element to an axis-aligned bounding box in world space.</param>
        /// <param name="bucketSize">Max elements per leaf.</param>
        /// <param name="branchFactor">Approximate number of children per internal node (≥2).</param>
        /// <exception cref="ArgumentNullException">Thrown when points or elementTransformer are null.</exception>
        public RTree3D(
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
            float minZ = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            float maxZ = float.MinValue;
            bool hasElements = false;

            for (int i = 0; i < elementCount; ++i)
            {
                T element = elements[i];

                Bounds elementBounds = transformer(element);
                /*
                    Inclusive-max, the same conversion every query applies to its own box. With a
                    half-open element extent an element whose max face lies exactly on the query's
                    min plane reads as not touching, while RTree2D's closed intersection returns it
                    -- the second half of the 2D/3D split in #658.
                */
                BoundingBox3D elementBox = BoundingBox3D.FromClosedBoundsInclusiveMax(
                    elementBounds
                );
                ElementData data = default;
                data._value = element;
                data._bounds = elementBox;
                data._center = elementBounds.center;
                data._insertionIndex = i;
                elementData[i] = data;
                Vector3 min = elementBox.min;
                Vector3 max = elementBox.max;

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

                if (min.z < minZ)
                {
                    minZ = min.z;
                }

                if (maxX < max.x)
                {
                    maxX = max.x;
                }

                if (maxY < max.y)
                {
                    maxY = max.y;
                }

                if (maxZ < max.z)
                {
                    maxZ = max.z;
                }
            }

            BoundingBox3D bounds = hasElements
                ? new BoundingBox3D(new Vector3(minX, minY, minZ), new Vector3(maxX, maxY, maxZ))
                : BoundingBox3D.Empty;

            if (hasElements)
            {
                bounds = bounds.EnsureMinimumSize(MinimumNodeSize);
            }

            _bounds = bounds;
            if (!hasElements)
            {
                _head = RTreeNode.CreateEmpty();
                return;
            }

            float rangeX = maxX - minX;
            float rangeY = maxY - minY;
            float rangeZ = maxZ - minZ;
            float inverseRangeX = float.Epsilon < rangeX ? 1f / rangeX : 0f;
            float inverseRangeY = float.Epsilon < rangeY ? 1f / rangeY : 0f;
            float inverseRangeZ = float.Epsilon < rangeZ ? 1f / rangeZ : 0f;

            for (int i = 0; i < elementCount; ++i)
            {
                ref ElementData data = ref elementData[i];
                Vector3 center = data._center;
                float normalizedX = (center.x - minX) * inverseRangeX;
                float normalizedY = (center.y - minY) * inverseRangeY;
                float normalizedZ = (center.z - minZ) * inverseRangeZ;
                ushort quantizedX = QuantizeNormalized(normalizedX);
                ushort quantizedY = QuantizeNormalized(normalizedY);
                ushort quantizedZ = QuantizeNormalized(normalizedZ);
                uint mortonKey = EncodeMorton(quantizedX, quantizedY, quantizedZ);
                data._sortKey = ComposeSortKey(mortonKey, quantizedX, quantizedY, quantizedZ);
            }

            if (1 < elementCount)
            {
                RadixSort(elementData, elementCount);
            }

            using PooledResource<List<RTreeNode>> currentLevelResource =
                Buffers<RTreeNode>.List.Get(out List<RTreeNode> currentLevel);

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

            RTreeNode head = 0 < currentLevel.Count ? currentLevel[0] : RTreeNode.CreateEmpty();

            _head = head;
            _bounds = _head.boundary;
        }

        private void CollectElementIndicesInBounds(BoundingBox3D bounds, List<int> indices)
        {
            indices.Clear();
            if (_head._count == 0)
            {
                return;
            }

            if (!bounds.Intersects(_bounds))
            {
                return;
            }

            using PooledResource<Stack<RTreeNode>> nodeBufferResource =
                Buffers<RTreeNode>.Stack.Get(out Stack<RTreeNode> nodesToVisit);
            nodesToVisit.Push(_head);

            while (nodesToVisit.TryPop(out RTreeNode currentNode))
            {
                if (!bounds.Intersects(currentNode.boundary))
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
                        if (bounds.Intersects(elementData._bounds))
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

                    if (!bounds.Intersects(child.boundary))
                    {
                        continue;
                    }

                    nodesToVisit.Push(child);
                }
            }
        }

        /// <summary>
        /// Finds all elements whose bounds fall within distance <paramref name="range"/> of
        /// <paramref name="position"/> (sphere query).
        /// </summary>
        /// <param name="position">Query center. A non-finite center returns no results.</param>
        /// <param name="range">Query radius, measured to the nearest point of an element's box, so
        /// zero returns exactly the elements whose box the query point touches. A negative or NaN
        /// radius returns nothing. The comparison is exact: no epsilon widens the sphere, because an
        /// absolute epsilon is most of a zero-radius query and nothing at all at world scale.</param>
        /// <param name="elementsInRange">Destination list, cleared exactly once before use.</param>
        /// <param name="minimumRange">Optional inner exclusion radius, compared the same way.</param>
        /// <returns>The destination list, for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="elementsInRange"/> is null.</exception>
        public List<T> GetElementsInRange(
            Vector3 position,
            float range,
            List<T> elementsInRange,
            float minimumRange = 0f
        )
        {
            if (elementsInRange == null)
            {
                throw new ArgumentNullException(nameof(elementsInRange));
            }

            elementsInRange.Clear();
            if (float.IsNaN(range) || range < 0f || !SpatialQueryMath.IsFinite(position))
            {
                return elementsInRange;
            }

            /*
                Inclusive-max, so an element sitting exactly on the +range face is still a candidate.
                A half-open [center - range, center + range) box drops it, and every axis-aligned
                neighbor of a grid-aligned query is exactly there.
            */
            BoundingBox3D queryBounds = BoundingBox3D.FromClosedBoundsInclusiveMax(
                new Bounds(position, new Vector3(range * 2f, range * 2f, range * 2f))
            );

            if (!queryBounds.Intersects(_bounds))
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

            float rangeSquared = range * range;
            bool hasMinimumRange = 0f < minimumRange;
            float minimumRangeSquared = minimumRange * minimumRange;

            foreach (int index in candidateIndices)
            {
                ElementData elementData = _elementData[index];
                float distanceSquared = elementData._bounds.DistanceSquaredTo(position);
                if (rangeSquared < distanceSquared)
                {
                    continue;
                }

                if (hasMinimumRange && distanceSquared <= minimumRangeSquared)
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
        /// <param name="bounds">Axis-aligned query bounds. The max face is inclusive, matching
        /// <see cref="KdTree3D{T}"/> and <see cref="OctTree3D{T}"/>, so a zero-size box finds every
        /// element touching it -- including an element built from a zero-size
        /// <see cref="Bounds"/>, whose extent is that point. A box with a NaN edge returns
        /// nothing.</param>
        /// <param name="elementsInBounds">Destination list, cleared exactly once before use.</param>
        /// <returns>The destination list, for chaining.</returns>
        /// <remarks>An element straddling the query boundary is returned, matching
        /// <see cref="RTree2D{T}"/> and this tree's own
        /// <see cref="GetElementsInRange(UnityEngine.Vector3,float,System.Collections.Generic.List{T},float)"/>,
        /// which measures to the element's box rather than its center. For the partitioning
        /// semantics that assign each element to exactly one region, use
        /// <see cref="GetElementsWithCentersInBounds"/>.</remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="elementsInBounds"/> is null.</exception>
        public List<T> GetElementsInBounds(Bounds bounds, List<T> elementsInBounds)
        {
            return CollectElementsInBounds(bounds, elementsInBounds, centersOnly: false);
        }

        /// <summary>
        /// Finds all elements whose <see cref="Bounds.center"/> lies inside the specified
        /// axis-aligned box.
        /// </summary>
        /// <param name="bounds">Axis-aligned query bounds. The max face is inclusive, so a zero-size
        /// box finds every element whose center is exactly on it. A box with a NaN edge returns
        /// nothing.</param>
        /// <param name="elementsWithCentersInBounds">Destination list, cleared exactly once before use.</param>
        /// <returns>The destination list, for chaining.</returns>
        /// <remarks>Each element belongs to exactly one region of a tiling, so a sweep over
        /// adjacent boxes visits it once. That is the opposite trade to
        /// <see cref="GetElementsInBounds"/>, which never omits an element that touches the
        /// box.</remarks>
        /// <exception cref="ArgumentNullException">Thrown when
        /// <paramref name="elementsWithCentersInBounds"/> is null.</exception>
        public List<T> GetElementsWithCentersInBounds(
            Bounds bounds,
            List<T> elementsWithCentersInBounds
        )
        {
            return CollectElementsInBounds(bounds, elementsWithCentersInBounds, centersOnly: true);
        }

        private List<T> CollectElementsInBounds(
            Bounds bounds,
            List<T> elementsInBounds,
            bool centersOnly
        )
        {
            if (elementsInBounds == null)
            {
                throw new ArgumentNullException(nameof(elementsInBounds));
            }

            elementsInBounds.Clear();
            if (SpatialQueryMath.IsInvalidQueryBounds(bounds))
            {
                return elementsInBounds;
            }

            BoundingBox3D queryBounds = BoundingBox3D.FromClosedBoundsInclusiveMax(bounds);
            if (!queryBounds.Intersects(_bounds))
            {
                return elementsInBounds;
            }

            using PooledResource<List<int>> indicesResource = Buffers<int>.List.Get(
                out List<int> indices
            );
            CollectElementIndicesInBounds(queryBounds, indices);
            foreach (int index in indices)
            {
                ElementData elementData = _elementData[index];
                if (centersOnly && !queryBounds.Contains(elementData._center))
                {
                    continue;
                }

                elementsInBounds.Add(elementData._value);
            }

            return elementsInBounds;
        }

        /// <summary>
        /// Returns an approximate set of the nearest <paramref name="count"/> neighbors to <paramref name="position"/>.
        /// </summary>
        /// <param name="position">Query center. A non-finite center returns no results.</param>
        /// <param name="count">How many neighbors to return. Zero or fewer returns nothing.</param>
        /// <param name="nearestNeighbors">Destination list, cleared exactly once before use.</param>
        /// <returns>The destination list, for chaining.</returns>
        /// <remarks>
        /// <para>Returns exactly <c>min(count, elementCount)</c> entries. Equal-valued elements stay
        /// distinct: identity is the element's insertion index, not its value. What comes back is
        /// ordered by ascending distance and then by ascending insertion index.</para>
        /// <para><b>Which</b> equidistant elements come back is a separate question, and it is not
        /// specified. This tree admits a candidate only when it is strictly closer than the current
        /// worst, so among equidistant elements the one the traversal reaches first wins -- for an
        /// R-tree that is Morton-curve order, which no caller should depend on. The
        /// collect-then-sort trees (<see cref="KdTree3D{T}"/>) resolve the same tie the other way,
        /// by lowest insertion index among whatever the descent visited.</para>
        /// <para><b>Cost:</b> a best-first descent, keyed on each node's distance to
        /// <paramref name="position"/>, that stops once <paramref name="count"/> candidates are held
        /// and the nearest unexpanded node is no closer than the worst of them. That makes the
        /// answer exact for the elements it indexes, and it makes a <paramref name="count"/> near
        /// the element count visit every leaf.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="nearestNeighbors"/> is null.</exception>
        public List<T> GetApproximateNearestNeighbors(
            Vector3 position,
            int count,
            List<T> nearestNeighbors
        )
        {
            if (nearestNeighbors == null)
            {
                throw new ArgumentNullException(nameof(nearestNeighbors));
            }

            nearestNeighbors.Clear();
            if (count <= 0 || _head._count == 0 || !SpatialQueryMath.IsFinite(position))
            {
                return nearestNeighbors;
            }

            using PooledResource<List<NodeDistance>> nodeHeapResource =
                Buffers<NodeDistance>.List.Get(out List<NodeDistance> nodeHeap);
            PushNode(nodeHeap, _head, position);

            using PooledResource<List<Candidate>> candidateBufferResource =
                Buffers<Candidate>.List.Get(out List<Candidate> candidates);
            float currentWorstDistanceSquared = float.PositiveInfinity;

            while (0 < nodeHeap.Count)
            {
                NodeDistance best = PopNode(nodeHeap);

                if (
                    count <= candidates.Count
                    && currentWorstDistanceSquared <= best._distanceSquared
                )
                {
                    break;
                }

                RTreeNode currentNode = best._node;
                if (!currentNode.isTerminal)
                {
                    RTreeNode[] childNodes = currentNode._children;
                    for (int i = 0; i < childNodes.Length; ++i)
                    {
                        RTreeNode child = childNodes[i];
                        if (0 < child._count)
                        {
                            PushNode(nodeHeap, child, position);
                        }
                    }
                    continue;
                }

                int startIndex = currentNode._startIndex;
                int endIndex = startIndex + currentNode._count;
                for (int i = startIndex; i < endIndex; ++i)
                {
                    ElementData elementData = _elementData[i];
                    float distanceSquared = (elementData._center - position).sqrMagnitude;

                    if (candidates.Count < count)
                    {
                        candidates.Add(
                            new Candidate(i, elementData._insertionIndex, distanceSquared)
                        );
                        if (candidates.Count == count)
                        {
                            currentWorstDistanceSquared = FindWorstDistance(candidates);
                        }

                        continue;
                    }

                    if (currentWorstDistanceSquared <= distanceSquared)
                    {
                        continue;
                    }

                    int worstCandidateIndex = FindIndexOfWorstCandidate(candidates);
                    candidates[worstCandidateIndex] = new Candidate(
                        i,
                        elementData._insertionIndex,
                        distanceSquared
                    );

                    currentWorstDistanceSquared = FindWorstDistance(candidates);
                }
            }

            if (candidates.Count == 0)
            {
                return nearestNeighbors;
            }

            candidates.Sort(CandidateComparer.Instance);
            int resultCount = Math.Min(count, candidates.Count);
            for (int i = 0; i < resultCount; ++i)
            {
                nearestNeighbors.Add(_elementData[candidates[i].elementIndex]._value);
            }
            return nearestNeighbors;
        }

        private static void PushNode(List<NodeDistance> heap, RTreeNode node, Vector3 point)
        {
            NodeDistance entry = new(node, node.boundary.DistanceSquaredTo(point));
            heap.Add(entry);
            int index = heap.Count - 1;

            while (0 < index)
            {
                int parent = (index - 1) >> 1;
                NodeDistance parentEntry = heap[parent];
                if (parentEntry._distanceSquared <= entry._distanceSquared)
                {
                    break;
                }

                heap[index] = parentEntry;
                index = parent;
            }

            heap[index] = entry;
        }

        private static NodeDistance PopNode(List<NodeDistance> heap)
        {
            int lastIndex = heap.Count - 1;
            NodeDistance result = heap[0];
            NodeDistance last = heap[lastIndex];
            heap.RemoveAt(lastIndex);

            int index = 0;
            int count = heap.Count;
            while (true)
            {
                int left = (index << 1) + 1;
                if (count <= left)
                {
                    break;
                }

                int right = left + 1;
                int smallest =
                    right < count && heap[right]._distanceSquared < heap[left]._distanceSquared
                        ? right
                        : left;

                if (last._distanceSquared <= heap[smallest]._distanceSquared)
                {
                    break;
                }

                heap[index] = heap[smallest];
                index = smallest;
            }

            if (0 < count)
            {
                heap[index] = last;
            }

            return result;
        }

        private static float FindWorstDistance(List<Candidate> list)
        {
            float worst = 0f;
            for (int i = 0; i < list.Count; ++i)
            {
                float distance = list[i].distanceSquared;
                if (worst < distance)
                {
                    worst = distance;
                }
            }

            return worst;
        }

        private static int FindIndexOfWorstCandidate(List<Candidate> list)
        {
            int worstIndex = 0;
            float worstDistanceSquared = list[0].distanceSquared;
            for (int i = 1; i < list.Count; ++i)
            {
                float distanceSquared = list[i].distanceSquared;
                if (worstDistanceSquared < distanceSquared)
                {
                    worstDistanceSquared = distanceSquared;
                    worstIndex = i;
                }
            }

            return worstIndex;
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

            /*
                Bounds-checked indexing throughout: the destination is a pooled, over-sized array,
                so an Unsafe.Add offset from element zero was bounded only by the prefix sum being
                right.
            */
            for (int shift = 0; shift < 64; shift += BitsPerPass)
            {
                counts.Clear();
                for (int i = 0; i < length; ++i)
                {
                    ulong key = source[i]._sortKey;
                    counts[(int)((key >> shift) & (BucketCount - 1))]++;
                }

                int total = 0;
                for (int bucket = 0; bucket < BucketCount; ++bucket)
                {
                    int count = counts[bucket];
                    counts[bucket] = total;
                    total += count;
                }

                for (int i = 0; i < length; ++i)
                {
                    ElementData value = source[i];
                    int bucket = (int)((value._sortKey >> shift) & (BucketCount - 1));
                    destination[counts[bucket]++] = value;
                }

                (source, destination) = (destination, source);
                dataInScratch = !dataInScratch;
            }

            if (dataInScratch)
            {
                Array.Copy(source, elements, length);
            }
        }

        private static BoundingBox3D CalculateBounds(
            ElementData[] elements,
            int startIndex,
            int count
        )
        {
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float minZ = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            float maxZ = float.MinValue;
            int endIndex = startIndex + count;
            for (int i = startIndex; i < endIndex; ++i)
            {
                BoundingBox3D bounds = elements[i]._bounds;
                Vector3 min = bounds.min;
                Vector3 max = bounds.max;
                minX = Math.Min(minX, min.x);
                maxX = Math.Max(maxX, max.x);
                minY = Math.Min(minY, min.y);
                maxY = Math.Max(maxY, max.y);
                minZ = Math.Min(minZ, min.z);
                maxZ = Math.Max(maxZ, max.z);
            }

            BoundingBox3D nodeBounds = new(
                new Vector3(minX, minY, minZ),
                new Vector3(maxX, maxY, maxZ)
            );

            return EnsureMinimumBounds(nodeBounds);
        }

        private static BoundingBox3D EnsureMinimumBounds(BoundingBox3D bounds)
        {
            return bounds.EnsureMinimumSize(MinimumNodeSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint EncodeMorton(ushort quantizedX, ushort quantizedY, ushort quantizedZ)
        {
            uint mortonX = Part1By2(quantizedX);
            uint mortonY = Part1By2(quantizedY);
            uint mortonZ = Part1By2(quantizedZ);
            return mortonX | (mortonY << 1) | (mortonZ << 2);
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
                return 1023;
            }

            return (ushort)(normalized * 1023f + 0.5f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong ComposeSortKey(
            uint mortonKey,
            ushort quantizedX,
            ushort quantizedY,
            ushort quantizedZ
        )
        {
            return ((ulong)mortonKey << 32)
                | ((ulong)quantizedX << 20)
                | ((ulong)quantizedY << 10)
                | quantizedZ;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Part1By2(uint value)
        {
            value &= 0x000003ff;
            value = (value | (value << 16)) & 0xFF0000FF;
            value = (value | (value << 8)) & 0x0F00F00F;
            value = (value | (value << 4)) & 0xC30C30C3;
            value = (value | (value << 2)) & 0x49249249;
            return value;
        }

        [Serializable]
        internal struct ElementData
        {
            internal T _value;
            internal BoundingBox3D _bounds;
            internal Vector3 _center;
            internal ulong _sortKey;

            /*
                Survives the Morton sort, so two equal values stay distinguishable and a distance
                tie resolves the same way on every run.
            */
            internal int _insertionIndex;
        }

        [Serializable]
        public sealed class RTreeNode
        {
            public readonly BoundingBox3D boundary;
            internal readonly RTreeNode[] _children;
            internal readonly int _startIndex;
            internal readonly int _count;
            public readonly bool isTerminal;

            private RTreeNode(
                int startIndex,
                int count,
                BoundingBox3D boundary,
                RTreeNode[] children
            )
            {
                _startIndex = startIndex;
                _count = count;
                this.boundary = boundary;
                _children = children ?? Array.Empty<RTreeNode>();
                isTerminal = _children.Length == 0;
            }

            internal static RTreeNode CreateEmpty()
            {
                return new RTreeNode(0, 0, BoundingBox3D.Empty, Array.Empty<RTreeNode>());
            }

            internal static RTreeNode CreateLeaf(ElementData[] elements, int startIndex, int count)
            {
                BoundingBox3D nodeBounds = CalculateBounds(elements, startIndex, count);
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
                BoundingBox3D nodeBounds = children[0].boundary;
                for (int i = 1; i < children.Length; ++i)
                {
                    nodeBounds = nodeBounds.ExpandToInclude(children[i].boundary);
                }

                nodeBounds = EnsureMinimumBounds(nodeBounds);
                return new RTreeNode(startIndex, endIndex - startIndex, nodeBounds, children);
            }
        }

        private readonly struct NodeDistance
        {
            internal readonly RTreeNode _node;
            internal readonly float _distanceSquared;

            internal NodeDistance(RTreeNode node, float distanceSquared)
            {
                _node = node;
                _distanceSquared = distanceSquared;
            }
        }

        private readonly struct Candidate
        {
            internal readonly int elementIndex;
            internal readonly int insertionIndex;
            internal readonly float distanceSquared;

            internal Candidate(int elementIndex, int insertionIndex, float distanceSquared)
            {
                this.elementIndex = elementIndex;
                this.insertionIndex = insertionIndex;
                this.distanceSquared = distanceSquared;
            }
        }

        private sealed class CandidateComparer : IComparer<Candidate>
        {
            internal static readonly CandidateComparer Instance = new();

            public int Compare(Candidate x, Candidate y)
            {
                int byDistance = x.distanceSquared.CompareTo(y.distanceSquared);
                return byDistance == 0 ? x.insertionIndex.CompareTo(y.insertionIndex) : byDistance;
            }
        }
    }
}
