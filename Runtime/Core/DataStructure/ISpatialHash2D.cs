// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Disposable abstraction for 2D spatial hashes so DI/factory consumers can enforce lease cleanup.
    /// </summary>
    /// <typeparam name="T">Stored element type.</typeparam>
    /// <remarks>
    /// <para><b>Multiset semantics:</b> every insert is kept, so an item inserted twice is returned
    /// twice unless a query is asked for <c>distinct</c> results.</para>
    /// <para><b>Total queries:</b> a negative radius, a NaN radius, a non-finite query position and
    /// an inverted or NaN rectangle all return the cleared, empty destination list.</para>
    /// <para><b>Unordered results:</b> a query returns the right multiset and says nothing about the
    /// order, so with <c>distinct: true</c> which of several comparer-equal items survives is not
    /// specified either.</para>
    /// </remarks>
    public interface ISpatialHash2D<T> : IDisposable
    {
        /// <summary>
        /// Gets the edge length of one grid cell.
        /// </summary>
        float CellSize { get; }

        /// <summary>
        /// Gets the number of occupied cells, which is a bucket count rather than an item count.
        /// </summary>
        int CellCount { get; }

        /// <summary>
        /// Inserts an item at the specified position, keeping every previous insert of it.
        /// </summary>
        /// <param name="position">World position of the item. A NaN or infinite component makes this
        /// a no-op rather than a throw; call <see cref="TryInsert"/> when you need to know.</param>
        /// <param name="item">The item to store.</param>
        void Insert(Vector2 position, T item);

        /// <summary>
        /// Inserts an item at the specified position and reports whether it was stored.
        /// </summary>
        /// <param name="position">World position of the item.</param>
        /// <param name="item">The item to store.</param>
        /// <returns><c>false</c>, having changed nothing, when <paramref name="position"/> has a NaN
        /// or infinite component; otherwise <c>true</c>.</returns>
        bool TryInsert(Vector2 position, T item);

        /// <summary>
        /// Removes one occurrence of an item stored at the specified position.
        /// </summary>
        /// <param name="position">The position the item was inserted at.</param>
        /// <param name="item">The item to remove.</param>
        /// <returns><c>true</c> when an occurrence was found and removed; <c>false</c> otherwise,
        /// including for a non-finite <paramref name="position"/>.</returns>
        bool Remove(Vector2 position, T item);

        /// <summary>
        /// Collects every item within <paramref name="radius"/> of <paramref name="position"/>.
        /// </summary>
        /// <param name="position">Query center. A non-finite center returns no results.</param>
        /// <param name="radius">Query radius. Zero returns only exact matches, a negative or NaN
        /// radius returns nothing, and positive infinity returns every stored item.</param>
        /// <param name="results">Destination list, cleared exactly once on every path.</param>
        /// <param name="distinct">Whether to de-duplicate using the hash's equality comparer.</param>
        /// <param name="exactDistance">When <c>false</c>, returns everything in the cells the query
        /// touches without the per-item distance check: a superset, and cheaper.</param>
        /// <returns>The destination list, for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="results"/> is null. A
        /// null destination is a bug in the calling code rather than bad data, so it is reported
        /// instead of being turned into a <see cref="NullReferenceException"/> from inside the
        /// traversal.</exception>
        List<T> Query(
            Vector2 position,
            float radius,
            List<T> results,
            bool distinct = true,
            bool exactDistance = true
        );

        /// <summary>
        /// Collects every item inside the specified rectangle, edges included.
        /// </summary>
        /// <param name="rect">Query rectangle. One with a NaN edge, or a max below its min, returns
        /// no results.</param>
        /// <param name="results">Destination list, cleared exactly once on every path.</param>
        /// <param name="distinct">Whether to de-duplicate using the hash's equality comparer.</param>
        /// <returns>The destination list, for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="results"/> is null. A
        /// null destination is a bug in the calling code rather than bad data, so it is reported
        /// instead of being turned into a <see cref="NullReferenceException"/> from inside the
        /// traversal.</exception>
        List<T> QueryRect(Rect rect, List<T> results, bool distinct = true);

        /// <summary>
        /// Removes every stored item and releases the buckets this instance rented.
        /// </summary>
        void Clear();
    }
}
