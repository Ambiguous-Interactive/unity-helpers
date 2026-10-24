// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
    using WallstopStudios.UnityHelpers.Utils;
#if !WALLSTOP_PROTO_ONLY
    using ProtoBuf;
#endif

    /// <summary>
    /// A disjoint-set (union-find) data structure with path compression and union by rank.
    /// Essential for determining connectivity in graphs, procedural generation (maze/terrain),
    /// and grouping/clustering algorithms. Near-constant time O(α(n)) operations where α is
    /// the inverse Ackermann function. Works with integer indices for maximum performance.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// DisjointSet islands = new DisjointSet(width * height);
    /// islands.TryUnion(cellA, cellB);
    /// bool sameRegion = islands.TryIsConnected(cellA, cellB, out bool connected) && connected;
    /// ]]></code>
    /// </example>
    [Serializable]
#if !WALLSTOP_PROTO_ONLY
    [ProtoContract]
#endif
    [WProtoContract]
    public sealed partial class DisjointSet
    {
        /// <summary>
        /// Gets the number of elements in the disjoint set.
        /// </summary>
        public int Count => _parent == null ? 0 : _parent.Length;

        /// <summary>
        /// Gets the number of distinct sets.
        /// </summary>
        public int SetCount => _setCount;

        [SerializeField]
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(1)]
#endif
        [WProtoMember(1)]
        private int[] _parent = Array.Empty<int>();

        [SerializeField]
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(2)]
#endif
        [WProtoMember(2)]
        private int[] _rank = Array.Empty<int>();

        [SerializeField]
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(3)]
#endif
        [WProtoMember(3)]
        private int _setCount;

        /// <summary>
        /// Constructs a disjoint set with n elements, each in its own set.
        /// </summary>
        /// <param name="n">The number of elements.</param>
        public DisjointSet(int n)
        {
            if (n <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(n), "Count must be positive.");
            }

            _parent = new int[n];
            _rank = new int[n];
            _setCount = n;

            for (int i = 0; i < n; ++i)
            {
                _parent[i] = i;
                _rank[i] = 0;
            }
        }

        private DisjointSet() { }

        internal static void CollectReusableSets<TElement>(
            List<List<TElement>> results,
            Stack<List<TElement>> reusable
        )
        {
            if (results.Count == 0)
            {
                return;
            }
            using PooledResource<HashSet<List<TElement>>> identityLease = SetBuffers<List<TElement>>
                .GetHashSetPool(ReferenceIdentityComparer<List<TElement>>.Instance)
                .Get(out HashSet<List<TElement>> identities);
            foreach (List<TElement> existing in results)
            {
                if (existing != null && identities.Add(existing))
                {
                    existing.Clear();
                    reusable.Push(existing);
                }
            }
            results.Clear();
        }

        internal static List<TElement> GetSetForRoot<TElement>(
            int root,
            int[] rootToResult,
            List<List<TElement>> results,
            Stack<List<TElement>> reusable
        )
        {
            int slot = rootToResult[root];
            if (0 < slot)
            {
                return results[slot - 1];
            }
            if (!reusable.TryPop(out List<TElement> destination))
            {
                destination = new List<TElement>();
            }
            results.Add(destination);
            rootToResult[root] = results.Count;
            return destination;
        }

        /// <summary>
        /// Attempts to find the representative (root) of the set containing element x.
        /// Uses path compression for optimization.
        /// </summary>
        /// <param name="x">The element to find.</param>
        /// <param name="representative">The representative if found.</param>
        /// <returns>True if x reaches a valid root; false for invalid indices, broken links, or cycles.</returns>
        public bool TryFind(int x, out int representative)
        {
            int[] parents = _parent;
            int elementCount = parents == null ? 0 : parents.Length;
            int current = x;
            for (int visited = 0; visited < elementCount; ++visited)
            {
                if (current < 0 || elementCount <= current)
                {
                    break;
                }

                int parent = parents[current];
                if (parent == current)
                {
                    representative = current;
                    while (x != current)
                    {
                        int next = parents[x];
                        parents[x] = current;
                        x = next;
                    }
                    return true;
                }
                current = parent;
            }

            representative = -1;
            return false;
        }

        /// <summary>
        /// Attempts to union the sets containing elements x and y.
        /// Uses union by rank for optimization.
        /// </summary>
        /// <param name="x">First element.</param>
        /// <param name="y">Second element.</param>
        /// <returns>True when different sets were joined; false for invalid indices, links, ranks, counts, or an existing connection.</returns>
        public bool TryUnion(int x, int y)
        {
            if (_rank == null || _rank.Length != Count || _setCount <= 1 || Count < _setCount)
            {
                return false;
            }
            if (!TryFind(x, out int rootX) || !TryFind(y, out int rootY))
            {
                return false;
            }

            if (rootX == rootY || _rank[rootX] < 0 || _rank[rootY] < 0)
            {
                return false;
            }

            // Union by rank
            if (_rank[rootX] < _rank[rootY])
            {
                _parent[rootX] = rootY;
            }
            else if (_rank[rootY] < _rank[rootX])
            {
                _parent[rootY] = rootX;
            }
            else
            {
                _parent[rootY] = rootX;
                if (_rank[rootX] < int.MaxValue)
                {
                    ++_rank[rootX];
                }
            }

            --_setCount;
            return true;
        }

        /// <summary>
        /// Attempts to check if elements x and y are in the same set.
        /// </summary>
        /// <param name="x">First element.</param>
        /// <param name="y">Second element.</param>
        /// <param name="connected">True if connected, false otherwise.</param>
        /// <returns>True if both indices are valid, false otherwise.</returns>
        public bool TryIsConnected(int x, int y, out bool connected)
        {
            if (!TryFind(x, out int rootX) || !TryFind(y, out int rootY))
            {
                connected = false;
                return false;
            }
            connected = rootX == rootY;
            return true;
        }

        /// <summary>
        /// Attempts to get the size of the set containing element x.
        /// </summary>
        public bool TryGetSetSize(int x, out int size)
        {
            if (!TryFind(x, out int root))
            {
                size = 0;
                return false;
            }

            int memberCount = 0;
            int parentLength = _parent.Length;
            for (int i = 0; i < parentLength; ++i)
            {
                if (TryFind(i, out int currentRoot) && currentRoot == root)
                {
                    ++memberCount;
                }
            }
            size = memberCount;
            return true;
        }

        /// <summary>
        /// Populates the provided list with all elements in the same set as element x.
        /// Clears the list before adding. Returns the same list for chaining.
        /// Leaves the list empty if x is invalid or its parent chain is malformed.
        /// </summary>
        public List<int> TryGetSet(int x, List<int> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();

            if (!TryFind(x, out int root))
            {
                return results;
            }

            int parentLength = _parent.Length;
            for (int i = 0; i < parentLength; ++i)
            {
                if (TryFind(i, out int currentRoot) && currentRoot == root)
                {
                    results.Add(i);
                }
            }
            return results;
        }

        /// <summary>
        /// Populates the provided list with all distinct sets.
        /// Reuses each nonnull input list at most once and returns distinct output lists for each set.
        /// Uses pooled temporary storage to avoid steady-state allocations.
        /// </summary>
        public List<List<int>> TryGetAllSets(List<List<int>> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            using PooledResource<Stack<List<int>>> reuseStackResource = Buffers<
                List<int>
            >.Stack.Get(out Stack<List<int>> reuseStack);
            CollectReusableSets(results, reuseStack);

            int elementCount = Count;
            if (elementCount == 0)
            {
                return results;
            }

            // Store resultIndex + 1 so zero means unseen; clear rented slots before their first read.
            using PooledArray<int> rootLease = SystemArrayPool<int>.Get(
                elementCount,
                clearArray: true,
                out int[] rootToResult
            );

            for (int i = 0; i < elementCount; ++i)
            {
                if (!TryFind(i, out int root))
                {
                    continue;
                }

                List<int> destination = GetSetForRoot(root, rootToResult, results, reuseStack);
                destination.Add(i);
            }

            return results;
        }

        /// <summary>
        /// Resets each delivered element to its own set, rebuilding missing rank storage.
        /// </summary>
        public void Reset()
        {
            int parentLength = Count;
            if (_parent == null)
            {
                _parent = Array.Empty<int>();
            }
            if (_rank == null || _rank.Length != parentLength)
            {
                _rank = new int[parentLength];
            }
            for (int i = 0; i < parentLength; ++i)
            {
                _parent[i] = i;
                _rank[i] = 0;
            }
            _setCount = parentLength;
        }
    }

    /// <summary>
    /// A generic disjoint-set (union-find) data structure that maps elements of type T to indices.
    /// Provides the same performance as <see cref="DisjointSet"/> with support for any element type.
    /// Uses a dictionary to map elements to internal indices.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// DisjointSet<string> rooms = new DisjointSet<string>(new[] { "Hall", "Kitchen", "Library" });
    /// rooms.TryUnion("Hall", "Kitchen");
    /// rooms.TryIsConnected("Hall", "Library", out bool linked);
    /// ]]></code>
    /// </example>
    [Serializable]
    public sealed class DisjointSet<T>
    {
        /// <summary>
        /// Gets the number of elements in the disjoint set.
        /// </summary>
        public int Count => _disjointSet.Count;

        /// <summary>
        /// Gets the number of distinct sets.
        /// </summary>
        public int SetCount => _disjointSet.SetCount;

        private readonly DisjointSet _disjointSet;
        private readonly Dictionary<T, int> _elementToIndex;
        private readonly List<T> _indexToElement;
        private readonly IEqualityComparer<T> _comparer;

        /// <summary>
        /// Constructs a disjoint set from a collection of elements.
        /// </summary>
        public DisjointSet(IEnumerable<T> elements, IEqualityComparer<T> comparer = null)
        {
            if (elements == null)
            {
                throw new ArgumentNullException(nameof(elements));
            }

            _comparer = comparer ?? EqualityComparer<T>.Default;
            _elementToIndex = new Dictionary<T, int>(_comparer);
            _indexToElement = new List<T>();

            foreach (T element in elements.AsValueEnumerableRequired())
            {
                if (_elementToIndex.TryAdd(element, _indexToElement.Count))
                {
                    _indexToElement.Add(element);
                }
            }

            if (_indexToElement.Count == 0)
            {
                throw new ArgumentException(
                    "Collection must contain at least one element.",
                    nameof(elements)
                );
            }

            _disjointSet = new DisjointSet(_indexToElement.Count);
        }

        /// <summary>
        /// Attempts to find the representative element of the set containing x.
        /// </summary>
        public bool TryFind(T x, out T representative)
        {
            if (!_elementToIndex.TryGetValue(x, out int index))
            {
                representative = default;
                return false;
            }

            if (!_disjointSet.TryFind(index, out int rootIndex))
            {
                representative = default;
                return false;
            }
            representative = _indexToElement[rootIndex];
            return true;
        }

        /// <summary>
        /// Attempts to union the sets containing elements x and y.
        /// </summary>
        /// <returns>True if the sets were unioned, false if invalid elements or already in same set.</returns>
        public bool TryUnion(T x, T y)
        {
            if (!_elementToIndex.TryGetValue(x, out int indexX))
            {
                return false;
            }
            if (!_elementToIndex.TryGetValue(y, out int indexY))
            {
                return false;
            }
            return _disjointSet.TryUnion(indexX, indexY);
        }

        /// <summary>
        /// Attempts to check if elements x and y are in the same set.
        /// </summary>
        public bool TryIsConnected(T x, T y, out bool connected)
        {
            if (!_elementToIndex.TryGetValue(x, out int indexX))
            {
                connected = false;
                return false;
            }

            if (!_elementToIndex.TryGetValue(y, out int indexY))
            {
                connected = false;
                return false;
            }

            return _disjointSet.TryIsConnected(indexX, indexY, out connected);
        }

        /// <summary>
        /// Attempts to get the size of the set containing element x.
        /// </summary>
        public bool TryGetSetSize(T x, out int size)
        {
            if (!_elementToIndex.TryGetValue(x, out int index))
            {
                size = 0;
                return false;
            }
            return _disjointSet.TryGetSetSize(index, out size);
        }

        /// <summary>
        /// Populates the provided list with all elements in the same set as x.
        /// Clears the list before adding. Returns the same list for chaining.
        /// Returns null if x is invalid.
        /// </summary>
        public List<T> TryGetSet(T x, List<T> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            if (!_elementToIndex.TryGetValue(x, out int index))
            {
                return results;
            }

            using PooledResource<List<int>> listResource = Buffers<int>.List.Get(
                out List<int> indices
            );
            foreach (int i in _disjointSet.TryGetSet(index, indices))
            {
                results.Add(_indexToElement[i]);
            }

            return results;
        }

        /// <summary>
        /// Populates distinct output lists with all sets, reusing each nonnull input list at most once.
        /// </summary>
        public List<List<T>> TryGetAllSets(List<List<T>> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            using PooledResource<Stack<List<T>>> stackResource = Buffers<List<T>>.Stack.Get(
                out Stack<List<T>> stack
            );
            DisjointSet.CollectReusableSets(results, stack);
            int elementCount = Count;
            using PooledArray<int> rootLease = SystemArrayPool<int>.Get(
                elementCount,
                clearArray: true,
                out int[] rootToResult
            );
            for (int index = 0; index < elementCount; ++index)
            {
                if (_disjointSet.TryFind(index, out int root))
                {
                    List<T> destination = DisjointSet.GetSetForRoot(
                        root,
                        rootToResult,
                        results,
                        stack
                    );
                    destination.Add(_indexToElement[index]);
                }
            }

            return results;
        }

        /// <summary>
        /// Resets the disjoint set so each element is in its own set.
        /// </summary>
        public void Reset()
        {
            _disjointSet.Reset();
        }
    }
}
