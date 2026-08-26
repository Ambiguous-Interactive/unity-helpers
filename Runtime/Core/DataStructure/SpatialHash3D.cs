// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// A 3D spatial hash for fast broad-phase collision detection and neighbor queries.
    /// Simpler and more efficient than octrees for uniformly distributed objects and frequently moving items.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// SpatialHash3D<Projectile> hash = new SpatialHash3D<Projectile>(1.5f);
    /// hash.Insert(projectile.Position, projectile);
    /// List<Projectile> nearby = new List<Projectile>();
    /// hash.QueryRange(origin, 3f, nearby);
    /// ]]></code>
    /// </example>
    /// <remarks>
    /// This structure is stable and production-ready. It supports distinct or non-distinct queries,
    /// exact-distance filtering, and efficient incremental updates for moving items.
    /// </remarks>
    [Serializable]
    public sealed class SpatialHash3D<T> : ISpatialHash3D<T>
    {
        private readonly Dictionary<FastVector3Int, EntryBucket> _grid;
        private readonly float _cellSize;
        private readonly IEqualityComparer<T> _comparer;

        /// <summary>
        /// Gets the cell size of the spatial hash.
        /// </summary>
        public float CellSize => _cellSize;

        /// <summary>
        /// Gets the total number of occupied cells.
        /// </summary>
        public int CellCount => _grid.Count;

        /// <summary>
        /// Constructs a 3D spatial hash with the specified cell size.
        /// </summary>
        public SpatialHash3D(float cellSize, IEqualityComparer<T> comparer = null)
        {
            if (cellSize <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cellSize),
                    "Cell size must be positive."
                );
            }

            _cellSize = cellSize;
            _comparer = comparer ?? EqualityComparer<T>.Default;
            _grid = new Dictionary<FastVector3Int, EntryBucket>();
        }

        /// <summary>
        /// Inserts an item at the specified position.
        /// </summary>
        public void Insert(Vector3 position, T item)
        {
            FastVector3Int cell = GetCell(position);
            if (!_grid.TryGetValue(cell, out EntryBucket bucket))
            {
                bucket = EntryBucket.Rent();
                _grid[cell] = bucket;
            }

            bucket.Entries.Add(new Entry(position, item));
        }

        /// <summary>
        /// Removes an item from the specified position.
        /// Returns true if found and removed.
        /// </summary>
        public bool Remove(Vector3 position, T item)
        {
            FastVector3Int cell = GetCell(position);
            if (!_grid.TryGetValue(cell, out EntryBucket bucket))
            {
                return false;
            }

            List<Entry> entries = bucket.Entries;

            for (int i = entries.Count - 1; 0 <= i; i--)
            {
                Entry entry = entries[i];
                if (!entry.position.Equals(position) || !_comparer.Equals(entry.item, item))
                {
                    continue;
                }

                // Swap-back: a bucket is an unordered set -- a query enumerates all of it --
                // and this loop returns immediately, so shifting the tail would buy nothing on a
                // path that runs whenever anything moves.
                entries.RemoveAtSwapBack(i);
                if (entries.Count == 0)
                {
                    _grid.Remove(cell);
                    bucket.Dispose();
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// Queries all items within the specified radius of the position.
        /// Clears the results list before adding. Returns the same list for chaining.
        /// </summary>
        /// <param name="position">The center position of the query.</param>
        /// <param name="radius">The radius to search within.</param>
        /// <param name="results">The list to store results in.</param>
        /// <param name="distinct">Whether to return distinct items only.</param>
        /// <param name="exactDistance">If true, performs exact distance checking. If false, returns all items in cells that intersect the query radius (faster but may include extra items).</param>
        public List<T> Query(
            Vector3 position,
            float radius,
            List<T> results,
            bool distinct = true,
            bool exactDistance = true
        )
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();

            int cellRadius = Mathf.CeilToInt(radius / _cellSize);
            FastVector3Int centerCell = GetCell(position);
            float radiusSquared = radius * radius;

            if (distinct)
            {
                using PooledResource<HashSet<T>> setResource = SetBuffers<T>
                    .GetHashSetPool(_comparer)
                    .Get(out HashSet<T> seen);

                for (int x = -cellRadius; x <= cellRadius; x++)
                {
                    for (int y = -cellRadius; y <= cellRadius; y++)
                    {
                        for (int z = -cellRadius; z <= cellRadius; z++)
                        {
                            FastVector3Int cell = new(
                                centerCell.x + x,
                                centerCell.y + y,
                                centerCell.z + z
                            );
                            if (!_grid.TryGetValue(cell, out EntryBucket bucket))
                            {
                                continue;
                            }

                            foreach (Entry entry in bucket.Entries)
                            {
                                if (!exactDistance)
                                {
                                    if (seen.Add(entry.item))
                                    {
                                        results.Add(entry.item);
                                    }
                                }
                                else
                                {
                                    float distSq = (entry.position - position).sqrMagnitude;
                                    if (distSq <= radiusSquared && seen.Add(entry.item))
                                    {
                                        results.Add(entry.item);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                for (int x = -cellRadius; x <= cellRadius; x++)
                {
                    for (int y = -cellRadius; y <= cellRadius; y++)
                    {
                        for (int z = -cellRadius; z <= cellRadius; z++)
                        {
                            FastVector3Int cell = new(
                                centerCell.x + x,
                                centerCell.y + y,
                                centerCell.z + z
                            );
                            if (!_grid.TryGetValue(cell, out EntryBucket bucket))
                            {
                                continue;
                            }

                            foreach (Entry entry in bucket.Entries)
                            {
                                if (!exactDistance)
                                {
                                    results.Add(entry.item);
                                }
                                else
                                {
                                    float distSq = (entry.position - position).sqrMagnitude;
                                    if (distSq <= radiusSquared)
                                    {
                                        results.Add(entry.item);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Queries all items within the specified box bounds.
        /// Clears the results list before adding. Returns the same list for chaining.
        /// </summary>
        public List<T> QueryBox(Bounds bounds, List<T> results, bool distinct = true)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();

            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            FastVector3Int minCell = GetCell(min);
            FastVector3Int maxCell = GetCell(max);

            if (distinct)
            {
                using PooledResource<HashSet<T>> setResource = SetBuffers<T>
                    .GetHashSetPool(_comparer)
                    .Get(out HashSet<T> seen);

                for (int x = minCell.x; x <= maxCell.x; x++)
                {
                    for (int y = minCell.y; y <= maxCell.y; y++)
                    {
                        for (int z = minCell.z; z <= maxCell.z; z++)
                        {
                            FastVector3Int cell = new(x, y, z);
                            if (!_grid.TryGetValue(cell, out EntryBucket bucket))
                            {
                                continue;
                            }

                            foreach (Entry entry in bucket.Entries)
                            {
                                Vector3 pos = entry.position;
                                if (
                                    min.x <= pos.x
                                    && pos.x <= max.x
                                    && min.y <= pos.y
                                    && pos.y <= max.y
                                    && min.z <= pos.z
                                    && pos.z <= max.z
                                )
                                {
                                    if (seen.Add(entry.item))
                                    {
                                        results.Add(entry.item);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                for (int x = minCell.x; x <= maxCell.x; x++)
                {
                    for (int y = minCell.y; y <= maxCell.y; y++)
                    {
                        for (int z = minCell.z; z <= maxCell.z; z++)
                        {
                            FastVector3Int cell = new(x, y, z);
                            if (!_grid.TryGetValue(cell, out EntryBucket bucket))
                            {
                                continue;
                            }

                            foreach (Entry entry in bucket.Entries)
                            {
                                Vector3 pos = entry.position;
                                if (
                                    min.x <= pos.x
                                    && pos.x <= max.x
                                    && min.y <= pos.y
                                    && pos.y <= max.y
                                    && min.z <= pos.z
                                    && pos.z <= max.z
                                )
                                {
                                    results.Add(entry.item);
                                }
                            }
                        }
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Clears all items from the spatial hash.
        /// </summary>
        public void Clear()
        {
            foreach (KeyValuePair<FastVector3Int, EntryBucket> kvp in _grid)
            {
                EntryBucket bucket = kvp.Value;
                bucket.Dispose();
            }
            _grid.Clear();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private FastVector3Int GetCell(Vector3 position)
        {
            return new FastVector3Int(
                Mathf.FloorToInt(position.x / _cellSize),
                Mathf.FloorToInt(position.y / _cellSize),
                Mathf.FloorToInt(position.z / _cellSize)
            );
        }

        public void Dispose()
        {
            Clear();
            SetBuffers<T>.DestroyHashSetPool(_comparer);
        }

        private readonly struct Entry
        {
            public readonly Vector3 position;
            public readonly T item;

            public Entry(Vector3 position, T item)
            {
                this.position = position;
                this.item = item;
            }
        }

        private struct EntryBucket : IDisposable
        {
            private List<Entry> _entries;
            private PooledResource<List<Entry>> _lease;

            public List<Entry> Entries => _entries;

            public static EntryBucket Rent()
            {
                EntryBucket bucket = default;
                bucket._lease = Buffers<Entry>.List.Get(out bucket._entries);
                return bucket;
            }

            public void Dispose()
            {
                if (_entries == null)
                {
                    return;
                }

                _lease.Dispose();
                _lease = default;
                _entries = null;
            }
        }
    }
}
