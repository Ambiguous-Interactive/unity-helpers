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
    /// A 2D spatial hash for fast broad-phase collision detection and neighbor queries.
    /// Simpler and more efficient than <see cref="QuadTree2D{T}"/> for uniformly distributed objects.
    /// Perfect for particle systems, entity proximity checks, and collision culling.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// SpatialHash2D<Enemy> hash = new SpatialHash2D<Enemy>(2f);
    /// hash.Insert(enemy.Position, enemy);
    /// List<Enemy> nearby = new List<Enemy>();
    /// hash.Query(playerPosition, 5f, nearby);
    /// ]]></code>
    /// </example>
    /// <remarks>
    /// <para><b>Multiset semantics:</b> every insert is kept, so the same item inserted twice is
    /// returned twice unless a query is asked for <c>distinct</c> results.</para>
    /// <para><b>Total queries:</b> a negative radius, a NaN radius, a non-finite query position and
    /// an inverted or NaN rectangle all return the cleared, empty destination list rather than an
    /// arbitrary subset. A radius of zero returns only exact matches; a radius of positive infinity
    /// returns every stored item.</para>
    /// <para><b>Unordered results:</b> a query returns the right multiset and says nothing about the
    /// order. Each query picks between walking the query's cells and walking the occupied buckets,
    /// whichever is smaller, so inserting into a far-away cell can change the order a later query
    /// enumerates in. With <c>distinct: true</c> that also decides <b>which</b> of several items the
    /// comparer calls equal survives de-duplication. Sort the destination yourself if you need one
    /// answer, and do not treat the surviving representative as chosen.</para>
    /// <para><b>A null destination throws <see cref="System.ArgumentNullException"/>.</b> That is a
    /// bug in the calling code rather than data the caller was handed, and the alternative is a bare
    /// <see cref="System.NullReferenceException"/> raised from inside the traversal, naming nothing.
    /// Do not "fix" it into a silent return.</para>
    /// </remarks>
    [Serializable]
    public sealed class SpatialHash2D<T> : ISpatialHash2D<T>
    {
        private readonly Dictionary<FastVector2Int, EntryBucket> _grid;
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
        /// Constructs a 2D spatial hash with the specified cell size.
        /// </summary>
        /// <param name="cellSize">Edge length of one grid cell. Must be finite and positive.</param>
        /// <param name="comparer">Equality comparer used by <see cref="Remove"/> and by distinct
        /// queries. Defaults to <see cref="EqualityComparer{T}.Default"/>.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="cellSize"/> is
        /// not a finite positive number. NaN and infinity are rejected: NaN collapses every insert
        /// into one bucket and infinity maps every position onto the same cell.</exception>
        public SpatialHash2D(float cellSize, IEqualityComparer<T> comparer = null)
        {
            if (!float.IsFinite(cellSize) || cellSize <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(cellSize),
                    cellSize,
                    "Cell size must be a finite, positive number."
                );
            }

            _cellSize = cellSize;
            _comparer = comparer ?? EqualityComparer<T>.Default;
            _grid = new Dictionary<FastVector2Int, EntryBucket>();
        }

        /// <summary>
        /// Inserts an item at the specified position. Repeated inserts of the same item are all
        /// kept; the hash has multiset semantics.
        /// </summary>
        /// <param name="position">World position of the item. A NaN or infinite component makes
        /// this a no-op: the hash is left unmodified and nothing is thrown, because a position that
        /// went non-finite in physics is data, not a call the caller got wrong. Use
        /// <see cref="TryInsert"/> when you need to know it happened.</param>
        /// <param name="item">The item to store.</param>
        public void Insert(Vector2 position, T item)
        {
            TryInsert(position, item);
        }

        /// <summary>
        /// Inserts an item at the specified position and reports whether it was stored.
        /// </summary>
        /// <param name="position">World position of the item.</param>
        /// <param name="item">The item to store.</param>
        /// <returns><c>false</c>, having changed nothing, when <paramref name="position"/> has a NaN
        /// or infinite component; <c>true</c> once the item is in a bucket.</returns>
        public bool TryInsert(Vector2 position, T item)
        {
            if (!SpatialQueryMath.IsFinite(position))
            {
                return false;
            }

            FastVector2Int cell = GetCell(position);
            if (!_grid.TryGetValue(cell, out EntryBucket bucket))
            {
                bucket = EntryBucket.Rent();
                _grid[cell] = bucket;
            }

            bucket.Entries.Add(new Entry(position, item));
            return true;
        }

        /// <summary>
        /// Removes one occurrence of an item stored at the specified position.
        /// </summary>
        /// <param name="position">The position the item was inserted at.</param>
        /// <param name="item">The item to remove.</param>
        /// <returns>True if an occurrence was found and removed; false otherwise, including when
        /// <paramref name="position"/> is not finite.</returns>
        public bool Remove(Vector2 position, T item)
        {
            if (!SpatialQueryMath.IsFinite(position))
            {
                return false;
            }

            FastVector2Int cell = GetCell(position);
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

                // Bucket order is unobserved, and immediate return makes swap-back safe.
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
        /// <param name="position">The center position of the query. A non-finite position returns
        /// no results.</param>
        /// <param name="radius">The radius to search within. Zero returns only exact matches, a
        /// negative or NaN radius returns nothing, and positive infinity returns every stored item.</param>
        /// <param name="results">The list to store results in. Cleared exactly once, on every path.</param>
        /// <param name="distinct">Whether to return distinct items only. When false the results are
        /// a multiset: an item inserted twice is returned twice.</param>
        /// <param name="exactDistance">If true, performs exact distance checking. If false, returns all items in cells that intersect the query radius (faster but may include extra items).</param>
        /// <returns>The destination list, for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="results"/> is null.</exception>
        public List<T> Query(
            Vector2 position,
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

            if (
                float.IsNaN(radius)
                || radius < 0f
                || !SpatialQueryMath.IsFinite(position)
                || _grid.Count == 0
            )
            {
                return results;
            }

            if (distinct)
            {
                using PooledResource<HashSet<T>> setResource = SetBuffers<T>
                    .GetHashSetPool(_comparer)
                    .Get(out HashSet<T> seen);
                CollectWithinRadius(position, radius, exactDistance, seen, results);
                return results;
            }

            CollectWithinRadius(position, radius, exactDistance, seen: null, results);
            return results;
        }

        /// <summary>
        /// Queries all items within the specified rectangular bounds.
        /// Clears the results list before adding. Returns the same list for chaining.
        /// </summary>
        /// <param name="rect">The rectangle to search. A rectangle with a NaN edge, or one whose
        /// max is below its min, returns no results.</param>
        /// <param name="results">The list to store results in. Cleared exactly once, on every path.</param>
        /// <param name="distinct">Whether to return distinct items only. When false the results are
        /// a multiset.</param>
        /// <returns>The destination list, for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="results"/> is null.</exception>
        public List<T> QueryRect(Rect rect, List<T> results, bool distinct = true)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            Vector2 min = rect.min;
            Vector2 max = rect.max;

            if (
                SpatialQueryMath.IsNaN(min)
                || SpatialQueryMath.IsNaN(max)
                || max.x < min.x
                || max.y < min.y
                || _grid.Count == 0
            )
            {
                return results;
            }

            if (distinct)
            {
                using PooledResource<HashSet<T>> setResource = SetBuffers<T>
                    .GetHashSetPool(_comparer)
                    .Get(out HashSet<T> seen);
                CollectWithinRect(min, max, seen, results);
                return results;
            }

            CollectWithinRect(min, max, seen: null, results);
            return results;
        }

        /// <summary>
        /// Clears all items from the spatial hash.
        /// </summary>
        public void Clear()
        {
            foreach (KeyValuePair<FastVector2Int, EntryBucket> kvp in _grid)
            {
                EntryBucket bucket = kvp.Value;
                bucket.Dispose();
            }
            _grid.Clear();
        }

        /// <summary>
        /// Releases the pooled buckets this instance owns. Shared pools are left alone: they are
        /// keyed by comparer instance, and the default comparer is a process-wide singleton, so
        /// destroying one would de-pool every other consumer of the same element type.
        /// </summary>
        public void Dispose()
        {
            Clear();
        }

        private void CollectWithinRadius(
            Vector2 position,
            float radius,
            bool exactDistance,
            HashSet<T> seen,
            List<T> results
        )
        {
            long cellRadius = SpatialQueryMath.CellRadiusFor(radius, _cellSize);
            FastVector2Int centerCell = GetCell(position);
            float radiusSquared = radius * radius;
            bool exactComparison = exactDistance && SpatialQueryMath.SquareSaturates(radius);
            double exactRadiusSquared = (double)radius * radius;
            long span = SpatialQueryMath.SpanForRadius(cellRadius);

            if (SpatialQueryMath.DenseScanIsCheaper(span, span, _grid.Count))
            {
                long minimumX = Math.Max(int.MinValue, centerCell.x - cellRadius);
                long maximumX = Math.Min(int.MaxValue, centerCell.x + cellRadius);
                long minimumY = Math.Max(int.MinValue, centerCell.y - cellRadius);
                long maximumY = Math.Min(int.MaxValue, centerCell.y + cellRadius);

                // Use 64-bit counters so an inclusive int.MaxValue bound cannot wrap the loop.
                for (long x = minimumX; x <= maximumX; ++x)
                {
                    for (long y = minimumY; y <= maximumY; ++y)
                    {
                        FastVector2Int cell = new((int)x, (int)y);
                        if (!_grid.TryGetValue(cell, out EntryBucket bucket))
                        {
                            continue;
                        }

                        AppendWithinRadius(
                            bucket.Entries,
                            position,
                            radiusSquared,
                            exactComparison,
                            exactRadiusSquared,
                            exactDistance,
                            seen,
                            results
                        );
                    }
                }

                return;
            }

            foreach (KeyValuePair<FastVector2Int, EntryBucket> kvp in _grid)
            {
                FastVector2Int cell = kvp.Key;
                if (cellRadius < Math.Abs((long)cell.x - centerCell.x))
                {
                    continue;
                }

                if (cellRadius < Math.Abs((long)cell.y - centerCell.y))
                {
                    continue;
                }

                AppendWithinRadius(
                    kvp.Value.Entries,
                    position,
                    radiusSquared,
                    exactComparison,
                    exactRadiusSquared,
                    exactDistance,
                    seen,
                    results
                );
            }
        }

        private static void AppendWithinRadius(
            List<Entry> entries,
            Vector2 position,
            float radiusSquared,
            bool exactComparison,
            double exactRadiusSquared,
            bool exactDistance,
            HashSet<T> seen,
            List<T> results
        )
        {
            foreach (Entry entry in entries)
            {
                if (exactComparison)
                {
                    double exactDistanceSquared = SpatialQueryMath.DistanceSquared(
                        entry.position,
                        position
                    );
                    if (exactRadiusSquared < exactDistanceSquared)
                    {
                        continue;
                    }
                }
                else if (exactDistance)
                {
                    float distanceSquared = (entry.position - position).sqrMagnitude;
                    if (radiusSquared < distanceSquared)
                    {
                        continue;
                    }
                }

                if (seen != null && !seen.Add(entry.item))
                {
                    continue;
                }

                results.Add(entry.item);
            }
        }

        private void CollectWithinRect(Vector2 min, Vector2 max, HashSet<T> seen, List<T> results)
        {
            FastVector2Int minCell = GetCell(min);
            FastVector2Int maxCell = GetCell(max);
            long spanX = SpatialQueryMath.SpanForRange(minCell.x, maxCell.x);
            long spanY = SpatialQueryMath.SpanForRange(minCell.y, maxCell.y);

            if (SpatialQueryMath.DenseScanIsCheaper(spanX, spanY, _grid.Count))
            {
                for (long x = minCell.x; x <= maxCell.x; ++x)
                {
                    for (long y = minCell.y; y <= maxCell.y; ++y)
                    {
                        FastVector2Int cell = new((int)x, (int)y);
                        if (!_grid.TryGetValue(cell, out EntryBucket bucket))
                        {
                            continue;
                        }

                        AppendWithinRect(bucket.Entries, min, max, seen, results);
                    }
                }

                return;
            }

            foreach (KeyValuePair<FastVector2Int, EntryBucket> kvp in _grid)
            {
                FastVector2Int cell = kvp.Key;
                if (cell.x < minCell.x || maxCell.x < cell.x)
                {
                    continue;
                }

                if (cell.y < minCell.y || maxCell.y < cell.y)
                {
                    continue;
                }

                AppendWithinRect(kvp.Value.Entries, min, max, seen, results);
            }
        }

        private static void AppendWithinRect(
            List<Entry> entries,
            Vector2 min,
            Vector2 max,
            HashSet<T> seen,
            List<T> results
        )
        {
            foreach (Entry entry in entries)
            {
                Vector2 pos = entry.position;
                if (pos.x < min.x || max.x < pos.x || pos.y < min.y || max.y < pos.y)
                {
                    continue;
                }

                if (seen != null && !seen.Add(entry.item))
                {
                    continue;
                }

                results.Add(entry.item);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private FastVector2Int GetCell(Vector2 position)
        {
            return new FastVector2Int(
                SpatialQueryMath.ToCellCoordinate(position.x, _cellSize),
                SpatialQueryMath.ToCellCoordinate(position.y, _cellSize)
            );
        }

        private readonly struct Entry
        {
            public readonly Vector2 position;
            public readonly T item;

            public Entry(Vector2 position, T item)
            {
                this.position = position;
                this.item = item;
            }
        }

        private readonly struct EntryBucket : IDisposable
        {
            private readonly List<Entry> _entries;
            private readonly PooledResource<List<Entry>> _lease;

            private EntryBucket(PooledResource<List<Entry>> lease, List<Entry> entries)
            {
                _lease = lease;
                _entries = entries;
            }

            public List<Entry> Entries => _entries;

            public static EntryBucket Rent()
            {
                PooledResource<List<Entry>> lease = Buffers<Entry>.List.Get(
                    out List<Entry> entries
                );
                return new EntryBucket(lease, entries);
            }

            public void Dispose()
            {
                _lease.Dispose();
            }
        }
    }
}
