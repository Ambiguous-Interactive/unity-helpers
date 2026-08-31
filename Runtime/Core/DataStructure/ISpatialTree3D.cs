// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Contract for 3D spatial trees (octrees, kd-trees, etc.) that expose range, bounds, and nearest-neighbor queries.
    /// Lets gameplay systems pick the most suitable spatial index without changing their query logic.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// ISpatialTree3D<Collider> tree = new OctTree3D<Collider>(worldBounds);
    /// List<Collider> results = new List<Collider>();
    /// tree.GetElementsInRange(playerPosition, 10f, results);
    /// ]]></code>
    /// </example>
    /// <typeparam name="T">The type of elements stored in the tree.</typeparam>
    /// <remarks>
    /// <para><b>⚠️ EXPERIMENTAL:</b> 3D spatial trees are currently experimental and under active development.</para>
    /// <para>APIs may change, and performance characteristics may vary. Use with caution in production environments.</para>
    /// <para><b>Result buffers:</b> Every query method clears the supplied <see cref="List{T}"/> before writing results. Pass a reusable buffer when you want to minimize allocations.</para>
    /// <para><b>Results are a multiset:</b> two elements with the same value are two results.</para>
    /// <para><b>Total queries:</b> a negative radius, a NaN radius, a non-finite query center and a
    /// box with a NaN edge or a max below its min all return the cleared, empty destination list.</para>
    /// </remarks>
    public interface ISpatialTree3D<T>
    {
        /// <summary>
        /// Gets the axis-aligned box that encloses every indexed element.
        /// </summary>
        Bounds Boundary { get; }

        /// <summary>
        /// Collects every element within <paramref name="range"/> of <paramref name="position"/>.
        /// </summary>
        /// <param name="position">Query center. A non-finite center returns no results.</param>
        /// <param name="range">Query radius. A negative or NaN radius returns nothing.</param>
        /// <param name="elementsInRange">Destination list, cleared exactly once on every path.</param>
        /// <param name="minimumRange">Optional inner exclusion radius; zero disables it.</param>
        /// <returns>The destination list, for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="elementsInRange"/> is
        /// null. A null destination is a bug in the calling code rather than bad data, so it is
        /// reported instead of being turned into a <see cref="NullReferenceException"/> from inside
        /// the traversal.</exception>
        List<T> GetElementsInRange(
            Vector3 position,
            float range,
            List<T> elementsInRange,
            float minimumRange = 0f
        );

        /// <summary>
        /// Collects every element whose extent touches the specified axis-aligned box, max faces
        /// included.
        /// </summary>
        /// <param name="bounds">Query box. One with a NaN edge, or a max below its min, returns no
        /// results; a zero-size box returns the elements sitting on it.</param>
        /// <remarks>An element's extent is its indexed shape: a point for the point-indexed
        /// structures, the element's box for the R-trees. So an element straddling the query
        /// boundary is returned rather than dropped, and the result is never missing a true hit.
        /// The R-trees also expose the opposite trade under its own name,
        /// <c>GetElementsWithCentersInBounds</c>.</remarks>
        /// <param name="elementsInBounds">Destination list, cleared exactly once on every path.</param>
        /// <returns>The destination list, for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="elementsInBounds"/>
        /// is null. A null destination is a bug in the calling code rather than bad data, so it is
        /// reported instead of being turned into a <see cref="NullReferenceException"/> from inside
        /// the traversal.</exception>
        List<T> GetElementsInBounds(Bounds bounds, List<T> elementsInBounds);

        /// <summary>
        /// Collects an approximate nearest-neighbor set around <paramref name="position"/>.
        /// </summary>
        /// <param name="position">Query center. A non-finite center returns no results.</param>
        /// <param name="count">How many neighbors to return. Zero or fewer returns nothing.</param>
        /// <param name="nearestNeighbors">Destination list, cleared exactly once on every path.</param>
        /// <returns>The destination list, holding exactly <c>min(count, elementCount)</c> entries
        /// ordered by ascending distance and then by ascending insertion index. <b>Which</b>
        /// equidistant elements are in that set is not specified, and differs by implementation:
        /// see the concrete type's remarks.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="nearestNeighbors"/>
        /// is null. A null destination is a bug in the calling code rather than bad data, so it is
        /// reported instead of being turned into a <see cref="NullReferenceException"/> from inside
        /// the traversal.</exception>
        List<T> GetApproximateNearestNeighbors(
            Vector3 position,
            int count,
            List<T> nearestNeighbors
        );
    }
}
